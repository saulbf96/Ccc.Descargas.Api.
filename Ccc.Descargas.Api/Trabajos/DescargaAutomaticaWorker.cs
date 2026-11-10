using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Agentes;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Trabajos;

/// <summary>
/// Descarga automática casi en tiempo real. Una tarea por CUENTA; las peticiones a CCC se forman en el carril de su
/// plataforma (CCC atiende una a la vez por plataforma), pero la espera de la BD de una cuenta no frena a las demás.
/// En cada turno una cuenta:
///   1. Si ya toca (cada MinutosEntreFotosAgentes): tiempos del día + foto de estatus de sus agentes.
///   2. Después de HoraCierreDia (1:00): repaso de las llamadas de AYER completas y cierre de sus tiempos.
///   3. Avanza sus llamadas por tramos desde su marcador hasta "ahora - MargenMinutos", máximo MinutosPorTurno.
/// El avance vive en la BD: si el IIS se apaga, al volver sigue donde se quedó (sin límite de horas).
/// La primera vez de una cuenta empieza a las 00:00 de ese día (nunca baja días anteriores).
/// Correos: en cuanto falla un tramo (máximo uno por hora y cuenta) y si una cuenta va más atrasada que
/// MinutosAtrasoAlerta (máximo uno por hora y cuenta).
/// </summary>
internal sealed class DescargaAutomaticaWorker(
    IServiceScopeFactory scopeFactory,
    ICuentaCccRepository cuentas,
    IOptions<DescargaOptions> options,
    IReporteDescargas reporte,
    INotificador notificador,
    TimeProvider timeProvider,
    ILogger<DescargaAutomaticaWorker> logger) : BackgroundService
{
    // Último correo por cuenta y motivo, para no mandar uno por minuto
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

        // Una tarea por CUENTA. Las peticiones a CCC se forman en el carril de su plataforma (CarrilesCcc), así que
        // CCC sigue recibiendo una a la vez por plataforma; pero mientras una cuenta espera a la BD (guardar,
        // bitácora), las demás de la misma plataforma pueden usar CCC. Si la BD está lenta, CCC no se queda parado.
        await Task.WhenAll(todas.Select(c => CorrerCuentaAsync(c, stoppingToken)));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!options.Value.Habilitada)
        {
            return;
        }

        // Al detenerse (reinicio o apagado del IIS / App Pool) queda constancia; al volver sigue desde su avance
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
        var estado = new EstadoCuenta();   // solo de esta cuenta (cada tarea la suya)

        while (!cancellationToken.IsCancellationRequested)
        {
            var hayPendientes = false;
            try
            {
                hayPendientes = await TurnoAsync(cuenta, estado, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Ej. sin conexión a la BD: no mata el proceso; se avisa (máximo un correo por hora) y sigue
                logger.LogError(ex, "Falló el turno de la cuenta CCC {ClientId}", cuenta.ClientId);
                await AvisarAsync(cuenta, "falla general", $"El turno de descarga de {cuenta.ClientId} {cuenta.Nombre} no se pudo " +
                    $"ejecutar: {ex.Message}. Se reintenta en el siguiente turno.", cancellationToken);
            }

            if (!hayPendientes)
            {
                // Cuenta al día (o con error): se espera un poco antes de volver a revisar
                await Task.Delay(TimeSpan.FromSeconds(o.SegundosEsperaSinPendientes), timeProvider, cancellationToken);
            }
        }

        logger.LogInformation("Descarga de la cuenta {ClientId} detenida", cuenta.ClientId);
    }

    /// <returns>true si a la cuenta todavía le faltan llamadas (se le dará otro turno sin esperar).</returns>
    private async Task<bool> TurnoAsync(CuentaCcc cuenta, EstadoCuenta estado, CancellationToken cancellationToken)
    {
        var o = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var agentes = scope.ServiceProvider.GetRequiredService<DescargarAgentesService>();
        var llamadas = scope.ServiceProvider.GetRequiredService<DescargarLlamadasService>();

        // 1. Foto de agentes (y tiempos del día en curso)
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

        // 2. Cierre de los días que falten (después de HoraCierreDia): repaso de llamadas de AYER + tiempos
        if (await agentes.DiaPorCerrarAsync(cuenta, cancellationToken) is { } dia)
        {
            await CerrarDiaAsync(cuenta, dia, estado, agentes, llamadas, cancellationToken);
        }

        // 3. Llamadas por tramos
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

        // Si hubo error no se le da otro turno inmediato (para no martillar a CCC); si avanzó y falta, sí
        return turno.Error is null && turno.Atraso > TimeSpan.FromSeconds(o.TramoMinimoSegundos);
    }

    /// <summary>
    /// Cierra un día. Si es AYER (y RepasarDiaAnterior): primero vuelve a pedir sus llamadas completas (repaso) y solo
    /// si el repaso sale bien cierra sus tiempos; así el marcador TIEMPOS_AGENTES también dice "este día ya se repasó".
    /// Días más viejos (estuvo apagada varios días) solo cierran tiempos: sus llamadas se bajaron ya tarde, completas.
    /// </summary>
    private async Task CerrarDiaAsync(CuentaCcc cuenta, DateOnly dia, EstadoCuenta estado,
        DescargarAgentesService agentes, DescargarLlamadasService llamadas, CancellationToken cancellationToken)
    {
        var o = options.Value;
        // DiaRepasado: si el repaso salió bien pero falló el cierre de tiempos, no se repite el repaso
        if (o.RepasarDiaAnterior && dia == DateOnly.FromDateTime(Ahora()).AddDays(-1) && estado.DiaRepasado != dia)
        {
            if (estado.UltimoRepasoFallido is { } fallo && Ahora() - fallo < EsperaTrasRepasoFallido)
            {
                return;   // falló hace poco: no se martilla a CCC con el día completo
            }

            var repaso = await llamadas.RepasarDiaAsync(cuenta, dia, cancellationToken);
            if (repaso is null)
            {
                return;   // la descarga normal todavía no pasa la medianoche: se cierra en un turno siguiente
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

    /// <summary>Lo que cada cuenta recuerda entre turnos (en memoria; lo importante vive en la BD).</summary>
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
