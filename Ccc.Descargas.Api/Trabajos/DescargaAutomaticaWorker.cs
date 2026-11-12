using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Agentes;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Trabajos;

// descarga automática: una tarea por cuenta (fotos de agentes, cierre del día y llamadas por tramos)
internal sealed class DescargaAutomaticaWorker(
    IServiceScopeFactory scopeFactory,
    ICuentaCccRepository cuentas,
    IOptions<DescargaOptions> options,
    IReporteDescargas reporte,
    INotificador notificador,
    TimeProvider timeProvider,
    ILogger<DescargaAutomaticaWorker> logger) : BackgroundService
{
    // último correo por cuenta y motivo
    private readonly Dictionary<(string ClientId, string Motivo), DateTime> ultimosCorreos = [];
    private readonly Lock candadoCorreos = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Habilitada)
        {
            logger.LogInformation("Descarga automática deshabilitada (DescargaAutomatica:Habilitada = false)");
            return;
        }

        var todas = cuentas.ObtenerTodas();
        var texto = $"La descarga automática inició en {Environment.MachineName}: {todas.Count} cuenta(s), " +
                    $"llamadas hasta ahora - {o.MargenMinutos} min, fotos de agentes cada {o.MinutosEntreFotosAgentes} min.";
        logger.LogInformation("{Texto}", texto);
        await reporte.EscribirAvisoAsync(null, texto, stoppingToken);

        // una tarea por cuenta, las peticiones a CCC hacen fila por plataforma
        await Task.WhenAll(todas.Select(c => CorrerCuentaAsync(c, stoppingToken)));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!options.Value.Habilitada)
        {
            return;
        }

        // al detenerse queda en el reporte
        var mensaje = $"La aplicación se detuvo en {Environment.MachineName} a las {Ahora():HH:mm}. " +
                      "Al volver a arrancar seguirá desde donde se quedó cada cuenta.";
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await reporte.EscribirAvisoAsync(null, mensaje, limite.Token);
            await notificador.EnviarAsync("La descarga automática se detuvo", mensaje, limite.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo avisar que la aplicación se detuvo");
        }
    }

    private async Task CorrerCuentaAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var estado = new EstadoCuenta();

        while (!cancellationToken.IsCancellationRequested)
        {
            var hayPendientes = false;
            try
            {
                hayPendientes = await TurnoAsync(cuenta, estado, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // si falla (ej. sin BD) se avisa y sigue
                logger.LogError(ex, "Falló el turno de la cuenta CCC {ClientId}", cuenta.ClientId);
                await AvisarAsync(cuenta, "falla general", $"El turno de descarga de {cuenta.ClientId} {cuenta.Nombre} no se pudo " +
                    $"ejecutar: {ex.Message}. Se reintenta en el siguiente turno.", cancellationToken);
            }

            if (!hayPendientes)
            {
                // al día o con error: espera un poco
                await Task.Delay(TimeSpan.FromSeconds(o.SegundosEsperaSinPendientes), timeProvider, cancellationToken);
            }
        }

        logger.LogInformation("Descarga de la cuenta {ClientId} detenida", cuenta.ClientId);
    }

    // true si todavía le faltan llamadas
    private async Task<bool> TurnoAsync(CuentaCcc cuenta, EstadoCuenta estado, CancellationToken cancellationToken)
    {
        var o = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var agentes = scope.ServiceProvider.GetRequiredService<DescargarAgentesService>();
        var llamadas = scope.ServiceProvider.GetRequiredService<DescargarLlamadasService>();

        // 1. foto de agentes y tiempos del día
        if (estado.UltimaFoto is not { } ultima || Ahora() - ultima >= TimeSpan.FromMinutes(o.MinutosEntreFotosAgentes))
        {
            estado.UltimaFoto = Ahora();
            var (tiempos, estatus) = await agentes.TomarFotoAsync(cuenta, OrigenDescarga.Automatica, cancellationToken);
            foreach (var r in new[] { tiempos, estatus }.Where(r => r is { Exitosa: false }))
            {
                await AvisarAsync(cuenta, TiposDescarga.Texto(r!.Tipo),
                    $"Falló {TiposDescarga.Texto(r.Tipo)} de {cuenta.ClientId} {cuenta.Nombre}: {r.Mensaje}", cancellationToken);
            }
        }

        // 2. cierre de días pendientes
        if (await agentes.DiaPorCerrarAsync(cuenta, cancellationToken) is { } dia)
        {
            await CerrarDiaAsync(cuenta, dia, estado, agentes, llamadas, cancellationToken);
        }

        // 3. llamadas por tramos
        var limite = Ahora().AddMinutes(-o.MargenMinutos);
        var turno = await llamadas.AvanzarAsync(cuenta, limite, cancellationToken);
        if (turno.Tramos > 0)
        {
            logger.LogInformation("CCC {ClientId}: {Tramos} tramo(s), {Llamadas} llamadas; descargado hasta {Hasta:dd/MM HH:mm:ss} (atraso {Atraso})",
                cuenta.ClientId, turno.Tramos, turno.Llamadas, turno.DescargadoHasta, ResumenDescargas.Duracion(turno.Atraso));
        }

        if (turno.Error is { } error)
        {
            await AvisarAsync(cuenta, "llamadas",
                $"Falló el tramo {error.Desde:dd/MM HH:mm:ss}-{error.Hasta:HH:mm:ss} de {cuenta.ClientId} {cuenta.Nombre}: {error.Mensaje}\n" +
                "Se reintenta solo en el siguiente turno; el avance no se movió.", cancellationToken);
        }

        if (turno.Atraso > TimeSpan.FromMinutes(o.MinutosAtrasoAlerta))
        {
            await AvisarAsync(cuenta, "atraso",
                $"La cuenta {cuenta.ClientId} {cuenta.Nombre} va {ResumenDescargas.Duracion(turno.Atraso)} atrasada " +
                $"(descargado hasta {turno.DescargadoHasta:dd/MM HH:mm}). Se sigue descargando.", cancellationToken);
        }

        // si hubo error no se repite de inmediato
        return turno.Error is null && turno.Atraso > TimeSpan.FromSeconds(o.TramoMinimoSegundos);
    }

    // cierra un día: si es ayer primero el repaso de llamadas y luego los tiempos
    private async Task CerrarDiaAsync(CuentaCcc cuenta, DateOnly dia, EstadoCuenta estado,
        DescargarAgentesService agentes, DescargarLlamadasService llamadas, CancellationToken cancellationToken)
    {
        var o = options.Value;
        // si el repaso ya salió no se repite
        if (o.RepasarDiaAnterior && dia == DateOnly.FromDateTime(Ahora()).AddDays(-1) && estado.DiaRepasado != dia)
        {
            if (estado.UltimoRepasoFallido is { } fallo && Ahora() - fallo < EsperaTrasRepasoFallido)
            {
                return;   // falló hace poco
            }

            var repaso = await llamadas.RepasarDiaAsync(cuenta, dia, cancellationToken);
            if (repaso is null)
            {
                return;   // la normal todavía no pasa la medianoche
            }

            if (repaso.TramosConError > 0)
            {
                estado.UltimoRepasoFallido = Ahora();
                await AvisarAsync(cuenta, "repaso", $"El repaso de llamadas del {dia:dd/MM} de {cuenta.ClientId} {cuenta.Nombre} tuvo " +
                    $"{repaso.TramosConError} tramo(s) con error. Se repite en {EsperaTrasRepasoFallido.TotalMinutes:0} min; " +
                    "los tiempos del día se cierran cuando el repaso salga completo.", cancellationToken);
                return;
            }

            estado.UltimoRepasoFallido = null;
            estado.DiaRepasado = dia;
        }

        var cierre = await agentes.CerrarDiaAsync(cuenta, dia, OrigenDescarga.Automatica, cancellationToken);
        if (!cierre.Exitosa)
        {
            await AvisarAsync(cuenta, "cierre", $"No se pudieron cerrar los tiempos del {dia:dd/MM} de {cuenta.ClientId}: {cierre.Mensaje}",
                cancellationToken);
        }
    }

    private static readonly TimeSpan EsperaTrasRepasoFallido = TimeSpan.FromMinutes(10);

    // estado de la cuenta entre turnos
    private sealed class EstadoCuenta
    {
        public DateTime? UltimaFoto { get; set; }

        public DateTime? UltimoRepasoFallido { get; set; }

        public DateOnly? DiaRepasado { get; set; }
    }

    private async Task AvisarAsync(CuentaCcc cuenta, string motivo, string mensaje, CancellationToken cancellationToken)
    {
        try
        {
            await reporte.EscribirAvisoAsync(cuenta.ClientId, mensaje.ReplaceLineEndings(" "), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo escribir el aviso en el reporte .txt");
        }

        lock (candadoCorreos)
        {
            if (ultimosCorreos.TryGetValue((cuenta.ClientId, motivo), out var ultimo) && Ahora() - ultimo < TimeSpan.FromHours(1))
            {
                return;   // ya se avisó en la última hora
            }

            ultimosCorreos[(cuenta.ClientId, motivo)] = Ahora();
        }

        try
        {
            await notificador.EnviarAsync($"[REVISAR] Descarga CCC {cuenta.ClientId}: {motivo}",
                $"Servidor: {Environment.MachineName}\nHora: {Ahora():dd/MM/yyyy HH:mm}\n\n{mensaje}\n\n" +
                "Detalle en la carpeta 'reportes' de la aplicación (un .txt por cuenta y por día).", cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo mandar el correo de aviso");
        }
    }

    private DateTime Ahora() => timeProvider.GetLocalNow().DateTime;
}
