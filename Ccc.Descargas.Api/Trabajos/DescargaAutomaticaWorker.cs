using System.Collections.Concurrent;
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
    EstadoDescargas estadoDescargas,
    TimeProvider timeProvider,
    ILogger<DescargaAutomaticaWorker> logger) : BackgroundService
{
    // último correo por cuenta y motivo
    private readonly Dictionary<(string ClientId, string Motivo), DateTime> ultimosCorreos = [];
    private readonly Lock candadoCorreos = new();

    // estado de cada cuenta, para avisar cuando todas queden al día
    private readonly ConcurrentDictionary<string, EstadoCuenta> estados = new();
    private readonly Lock candadoAlDia = new();
    private bool avisoTodasAlDia;
    private int totalCuentas;

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
        totalCuentas = todas.Count;

        // correo de arranque con cómo quedó cada cuenta, aparte para no frenar la descarga
        var correoInicio = EnviarEstadoAsync("La descarga automática inició",
            $"{texto}\nLo que vaya más de {o.UmbralSaltoMinutos} min atrasado salta a la hora actual y se recupera solo, aparte.",
            TimeSpan.FromMinutes(3), stoppingToken);

        // una tarea por cuenta, las peticiones a CCC hacen fila por plataforma
        await Task.WhenAll(todas.Select(c => CorrerCuentaAsync(c, stoppingToken)).Append(correoInicio));
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
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await reporte.EscribirAvisoAsync(null, mensaje, limite.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo escribir en el reporte que la aplicación se detuvo");
        }

        await EnviarEstadoAsync("La descarga automática se detuvo", mensaje, TimeSpan.FromSeconds(15), CancellationToken.None);
    }

    // correo con el estado de todas las cuentas; si la BD no responde a tiempo se manda solo el texto
    private async Task EnviarEstadoAsync(string titulo, string texto, TimeSpan espera, CancellationToken cancellationToken)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(espera);
        var ahora = Ahora();
        string asunto, cuerpo;
        try
        {
            var correo = await estadoDescargas.ArmarAsync(titulo, ahora.Date, ahora, limite.Token);
            asunto = correo.Asunto;
            cuerpo = $"{texto}\n\n{correo.Cuerpo}";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo armar el estado de las cuentas para el correo: {Titulo}", titulo);
            asunto = $"[REVISAR] {titulo}";
            cuerpo = $"{texto}\n\nNo se pudo leer el estado de las cuentas en la base de datos: {ex.Message}";
        }

        try
        {
            using var envio = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await notificador.EnviarAsync(asunto, cuerpo, envio.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo mandar el correo: {Titulo}", titulo);
        }
    }

    // un solo correo cuando todas quedan al día; se vuelve a armar si alguna se atrasa de verdad
    private async Task RevisarTodasAlDiaAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        lock (candadoAlDia)
        {
            var todasAlDia = estados.Count == totalCuentas && estados.Values.All(e => e.AlDia && e.TieneHuecos == false);
            if (!todasAlDia)
            {
                if (avisoTodasAlDia && estados.Values.Any(e => e.TieneHuecos == true || e.Atraso > TimeSpan.FromMinutes(o.MinutosAtrasoAlerta)))
                {
                    avisoTodasAlDia = false;
                }

                return;
            }

            if (avisoTodasAlDia)
            {
                return;
            }

            avisoTodasAlDia = true;
        }

        logger.LogInformation("Todas las cuentas quedaron al día, sin atrasos por recuperar");
        await reporte.EscribirAvisoAsync(null, "Todas las cuentas quedaron al día, sin atrasos por recuperar.", cancellationToken);
        await EnviarEstadoAsync("Todas las cuentas al día",
            $"A las {Ahora():HH:mm} todas las cuentas quedaron al día y sin atrasos por recuperar.",
            TimeSpan.FromMinutes(3), cancellationToken);
    }

    private async Task CorrerCuentaAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var estado = new EstadoCuenta();
        estados[cuenta.ClientId] = estado;

        while (!cancellationToken.IsCancellationRequested)
        {
            var hayPendientes = false;
            try
            {
                hayPendientes = await TurnoAsync(cuenta, estado, cancellationToken);
                await RevisarTodasAlDiaAsync(cancellationToken);
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

        // 3. llamadas en vivo por tramos (si va muy atrasada salta a la hora actual y lo atrasado queda como hueco)
        var limite = Ahora().AddMinutes(-o.MargenMinutos);
        var turno = await llamadas.AvanzarAsync(cuenta, limite, cancellationToken);
        estado.Atraso = turno.Atraso;
        estado.AlDia = turno.Error is null && turno.Atraso <= TimeSpan.FromMinutes(o.MargenMinutos);
        if (turno.SaltoDesde is { } saltoDesde)
        {
            estado.TieneHuecos = true;
            await AvisarAsync(cuenta, "recuperación", $"La cuenta {cuenta.ClientId} {cuenta.Nombre} iba atrasada desde el " +
                $"{saltoDesde:dd/MM HH:mm}. Las llamadas en vivo siguen a la par desde las {limite:dd/MM HH:mm} y lo atrasado " +
                $"({ResumenDescargas.Duracion(limite - saltoDesde)}) se recupera solo, aparte.", cancellationToken);
        }

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

        var vivoPendiente = turno.Error is null && turno.Atraso > TimeSpan.FromSeconds(o.TramoMinimoSegundos);
        if (vivoPendiente || turno.Error is not null)
        {
            return vivoPendiente;   // el vivo va primero
        }

        // 4. con el vivo al día, lo que sobra del turno es para recuperar huecos
        var recuperacion = await llamadas.RecuperarAsync(cuenta, cancellationToken);
        estado.TieneHuecos = recuperacion is not null;
        if (recuperacion is null)
        {
            if (estado.Recuperando)
            {
                estado.Recuperando = false;
                logger.LogInformation("CCC {ClientId}: terminó de recuperar lo atrasado", cuenta.ClientId);
                var aviso = $"La cuenta {cuenta.ClientId} {cuenta.Nombre} terminó de recuperar lo atrasado y va al día " +
                            $"(llamadas descargadas hasta las {turno.DescargadoHasta:dd/MM HH:mm}).";
                await reporte.EscribirAvisoAsync(cuenta.ClientId, aviso, cancellationToken);
                await notificador.EnviarAsync($"[OK] Descarga CCC {cuenta.ClientId}: terminó de recuperar lo atrasado",
                    $"Servidor: {Environment.MachineName}\nHora: {Ahora():dd/MM/yyyy HH:mm}\n\n{aviso}", cancellationToken);
            }

            return false;
        }

        estado.Recuperando = true;
        var rec = recuperacion.Turno;
        if (rec.Tramos > 0)
        {
            logger.LogInformation("CCC {ClientId} recuperación: {Tramos} tramo(s), {Llamadas} llamadas; va en {Hasta:dd/MM HH:mm:ss}, faltan {Pendiente} en {Huecos} hueco(s)",
                cuenta.ClientId, rec.Tramos, rec.Llamadas, rec.DescargadoHasta, ResumenDescargas.Duracion(recuperacion.Pendiente), recuperacion.Huecos);
        }

        if (rec.Error is { } errorRecuperacion)
        {
            await AvisarAsync(cuenta, "recuperación con error",
                $"Falló el tramo de recuperación {errorRecuperacion.Desde:dd/MM HH:mm:ss}-{errorRecuperacion.Hasta:HH:mm:ss} de " +
                $"{cuenta.ClientId} {cuenta.Nombre}: {errorRecuperacion.Mensaje}\nSe reintenta solo; el hueco no se movió.", cancellationToken);
            return false;
        }

        return true;
    }

    // cierra un día: primero el repaso de las llamadas que se bajaron en vivo y luego los tiempos
    private async Task CerrarDiaAsync(CuentaCcc cuenta, DateOnly dia, EstadoCuenta estado,
        DescargarAgentesService agentes, DescargarLlamadasService llamadas, CancellationToken cancellationToken)
    {
        var o = options.Value;
        // si el repaso ya salió no se repite
        if (o.RepasarDiaAnterior && estado.DiaRepasado != dia)
        {
            if (estado.UltimoRepasoFallido is { } fallo && Ahora() - fallo < EsperaTrasRepasoFallido)
            {
                return;   // falló hace poco
            }

            var repaso = await llamadas.RepasarDiaAsync(cuenta, dia, cancellationToken);
            if (repaso is null)
            {
                return;   // el día todavía no está todo descargado (vivo antes de medianoche o hueco pendiente)
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

        // tiene huecos por recuperar (para avisar cuando termine)
        public bool Recuperando { get; set; }

        // vivo al día y sin error en el último turno
        public bool AlDia { get; set; }

        public TimeSpan Atraso { get; set; }

        // null = todavía no se sabe (no se ha revisado la recuperación)
        public bool? TieneHuecos { get; set; }
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
