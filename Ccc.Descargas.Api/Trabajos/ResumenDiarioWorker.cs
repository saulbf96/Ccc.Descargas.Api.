using System.ComponentModel.DataAnnotations;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Trabajos;

// ResumenDiario: dos correos al día para saber que la descarga sigue viva
public sealed class ResumenDiarioOptions
{
    public const string Seccion = "ResumenDiario";

    public bool Habilitado { get; init; } = true;

    // correo de inicio del día
    [Required]
    public TimeSpan HoraInicioDia { get; init; } = new(7, 0, 0);

    // correo de fin del día (resumen de ayer)
    [Required]
    public TimeSpan HoraFinDia { get; init; } = new(2, 0, 0);
}

internal sealed class ResumenDiarioWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ResumenDiarioOptions> options,
    IOptions<DescargaOptions> descarga,
    INotificador notificador,
    TimeProvider timeProvider,
    ILogger<ResumenDiarioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Habilitado || !descarga.Value.Habilitada)
        {
            logger.LogInformation("Correos de inicio y fin del día deshabilitados");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var (cuando, esInicio) = SiguienteEnvio(Ahora());
            await Task.Delay(cuando - Ahora(), timeProvider, stoppingToken);
            await EnviarAsync(esInicio, stoppingToken);
        }
    }

    private async Task EnviarAsync(bool esInicio, CancellationToken cancellationToken)
    {
        var ahora = Ahora();
        var hoy = ahora.Date;
        // inicio: hoy hasta ahora, fin: ayer completo
        var (titulo, desde, hasta) = esInicio
            ? ($"Descarga CCC activa - inicio del día {hoy:dd/MM}", hoy, ahora)
            : ($"Resumen del día {hoy.AddDays(-1):dd/MM}", hoy.AddDays(-1), hoy.AddSeconds(-1));

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cuentas = scope.ServiceProvider.GetRequiredService<ICuentaCccRepository>().ObtenerTodas();
            var bitacora = scope.ServiceProvider.GetRequiredService<IBitacoraDescargas>();

            var resumenes = new List<(CuentaCcc, ResumenCuenta)>();
            foreach (var cuenta in cuentas)
            {
                resumenes.Add((cuenta, await bitacora.ObtenerResumenAsync(cuenta.ClientId, desde, hasta, cancellationToken)));
            }

            var o = descarga.Value;
            var correo = ResumenDescargas.Armar(titulo, resumenes, desde, hasta, ahora, ahora.AddMinutes(-o.MargenMinutos),
                TimeSpan.FromMinutes(o.MinutosAtrasoAlerta), Environment.MachineName);
            await notificador.EnviarAsync(correo.Asunto, correo.Cuerpo, cancellationToken);
            logger.LogInformation("Correo de {Tipo} del día enviado: {Asunto}", esInicio ? "inicio" : "fin", correo.Asunto);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "No se pudo armar el correo de {Tipo} del día", esInicio ? "inicio" : "fin");
            await notificador.EnviarAsync($"[REVISAR] {titulo}: no se pudo armar el resumen",
                $"Servidor: {Environment.MachineName}\nNo se pudo leer la bitácora: {ex.Message}\n" +
                "Probablemente no hay conexión con la base de datos; la descarga tampoco podrá guardar.",
                cancellationToken);
        }
    }

    // siguiente envío pendiente
    private (DateTime Cuando, bool EsInicio) SiguienteEnvio(DateTime ahora)
    {
        var inicio = Proximo(ahora, options.Value.HoraInicioDia);
        var fin = Proximo(ahora, options.Value.HoraFinDia);
        return inicio <= fin ? (inicio, true) : (fin, false);
    }

    private static DateTime Proximo(DateTime ahora, TimeSpan hora)
    {
        var hoy = ahora.Date + hora;
        return hoy > ahora ? hoy : hoy.AddDays(1);
    }

    private DateTime Ahora() => timeProvider.GetLocalNow().DateTime;
}
