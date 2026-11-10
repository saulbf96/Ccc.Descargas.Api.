using System.Text.Json;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Application.Llamadas;

/// <summary>
/// Descarga las llamadas de una cuenta POR TRAMOS ADAPTATIVOS (30 s a 15 min): pedir rangos chicos a CCC es
/// mucho más rápido que la hora completa. Cada tramo recorre todas sus páginas, se guarda y queda en la
/// bitácora. La automática sigue desde el MARCADOR DE AVANCE de la cuenta (sin límite de 24 h): si un tramo
/// falla, el avance no se mueve y en el siguiente turno se vuelve a pedir ese mismo tramo.
/// Guardar es idempotente: repetir un tramo actualiza lo que ya estaba y agrega lo nuevo.
/// </summary>
public sealed class DescargarLlamadasService(
    ICuentaCccRepository cuentas,
    ICccClient cccClient,
    ILlamadaRepository llamadas,
    IBitacoraDescargas bitacora,
    IReporteDescargas reporte,
    IOptions<DescargaOptions> options,
    TimeProvider timeProvider,
    ILogger<DescargarLlamadasService> logger)
{
    public const int TamanoPagina = 1000;            // máximo que regresa CCC por página
    public const int MaxPaginasPorTramo = 500;       // freno de seguridad (500 mil llamadas en un solo tramo)
    public static readonly TimeSpan RangoMaximoManual = TimeSpan.FromHours(24);

    /// <summary>
    /// Descarga automática: avanza la cuenta desde su marcador hasta <paramref name="limite"/> (ahora - margen),
    /// tramo por tramo, durante máximo MinutosPorTurno. Se detiene en el primer tramo con error.
    /// </summary>
    public async Task<ResultadoTurno> AvanzarAsync(CuentaCcc cuenta, DateTime limite, CancellationToken cancellationToken)
    {
        var o = options.Value;
        limite = TruncarASegundo(limite);   // la BD guarda segundos enteros: sin esto se redondearía
        var avance = await bitacora.ObtenerAvanceAsync(cuenta.ClientId, TipoDescarga.Llamadas, cancellationToken);
        // Primera vez de la cuenta (sin marcador): desde las 00:00 de ese día; nunca días anteriores ni la historia.
        // Después, siempre desde el marcador: si estuvo apagada, baja todo lo que faltó.
        var desde = avance?.DescargadoHasta ?? limite.Date;
        var tamano = TramosAdaptativos.Inicial(avance?.TamanoTramoSegundos, o);
        var finTurno = Ahora().AddMinutes(o.MinutosPorTurno);
        var inicioTurno = desde;

        int tramos = 0, total = 0, paginas = 0, insertadas = 0, actualizadas = 0;
        var inicio = Ahora();
        RegistroTramo? error = null;

        while (desde < limite && Ahora() < finTurno && !cancellationToken.IsCancellationRequested)
        {
            if (Min(desde.AddSeconds(tamano), limite).AddSeconds(-1) < desde)
            {
                break;   // falta menos de un segundo para el límite
            }

            (var registro, tamano) = await BajarTramoAsync(cuenta, desde, limite, tamano, OrigenDescarga.Automatica,
                moverAvance: true, etiqueta: null, cancellationToken);
            var hasta = registro.Hasta;   // incluido: 10:00:00 a 10:00:59

            if (!registro.Exitosa)
            {
                error = registro;
                await EscribirReporteAsync(registro, cancellationToken);
                break;
            }

            tramos++;
            total += registro.Registros;
            paginas += registro.Paginas;
            insertadas += registro.Insertadas;
            actualizadas += registro.Actualizadas;
            desde = hasta.AddSeconds(1);
            tamano = TramosAdaptativos.Siguiente(tamano, registro.Registros, registro.Paginas, o);
        }

        if (tramos > 0)
        {
            // Una línea por turno en el .txt (no una por tramo de 1 minuto)
            await EscribirReporteAsync(new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, inicioTurno,
                desde.AddSeconds(-1), tamano, OrigenDescarga.Automatica, true, paginas, total, insertadas, actualizadas,
                inicio, Ahora(), $"{tramos} tramo(s)"), cancellationToken);
        }

        return new ResultadoTurno(cuenta.ClientId, tramos, total, desde, limite, error);
    }

    /// <summary>
    /// Descarga MANUAL de un rango (máximo 24 h), por tramos. No mueve el marcador de avance de la automática.
    /// Un tramo con error no detiene los demás.
    /// </summary>
    public async Task<ResultadoDescarga?> DescargarRangoAsync(
        string clientId, DateTime desde, DateTime hasta, CancellationToken cancellationToken)
    {
        var cuenta = cuentas.ObtenerPorId(clientId);
        if (cuenta is null)
        {
            return null;
        }

        var resultado = await DescargarTramosAsync(cuenta, TruncarASegundo(desde), TruncarASegundo(hasta), OrigenDescarga.Manual,
            etiqueta: null, cancellationToken);
        logger.LogInformation("Descarga manual CCC {ClientId} {Desde:yyyy-MM-dd HH:mm}-{Hasta:yyyy-MM-dd HH:mm}: {Llamadas} llamadas, {Errores} tramos con error",
            clientId, desde, hasta, resultado.TotalLlamadas, resultado.TramosConError);
        return resultado;
    }

    /// <summary>
    /// REPASO del día completo (00:00:00 a 23:59:59), ya pasada la medianoche: vuelve a pedir todo el día a CCC para
    /// recoger lo que la descarga casi en tiempo real no alcanzó a ver (llamadas largas que entraron tarde al
    /// historial, tipificaciones capturadas después). Por call_id no duplica: inserta lo que faltaba y actualiza
    /// lo que cambió. No mueve el marcador. Regresa null si la descarga normal todavía no termina ese día.
    /// En la bitácora sus tramos quedan como AUTOMATICA con MENSAJE "Repaso del dd/MM".
    /// </summary>
    public async Task<ResultadoDescarga?> RepasarDiaAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken)
    {
        var inicioDia = dia.ToDateTime(TimeOnly.MinValue);
        var finDia = inicioDia.AddDays(1);
        var avance = await bitacora.ObtenerAvanceAsync(cuenta.ClientId, TipoDescarga.Llamadas, cancellationToken);
        if (avance is null || avance.DescargadoHasta < finDia)
        {
            return null;   // primero que la descarga normal pase la medianoche
        }

        var inicio = Ahora();
        var etiqueta = $"Repaso del {dia:dd/MM}";
        var resultado = await DescargarTramosAsync(cuenta, inicioDia, finDia, OrigenDescarga.Automatica, etiqueta, cancellationToken);

        // Una sola línea en el .txt por todo el repaso (los tramos con error ya se escribieron uno por uno)
        var errores = resultado.TramosConError;
        await EscribirReporteAsync(new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, inicioDia,
            finDia.AddSeconds(-1), null, OrigenDescarga.Automatica, errores == 0, resultado.Tramos.Sum(t => t.Paginas),
            resultado.TotalLlamadas, resultado.Tramos.Sum(t => t.Insertadas), resultado.Tramos.Sum(t => t.Actualizadas),
            inicio, Ahora(), errores == 0 ? etiqueta : $"{etiqueta}: {errores} tramo(s) con error; se repite el repaso"), cancellationToken);

        logger.LogInformation("{Etiqueta} CCC {ClientId}: {Llamadas} llamadas, {Nuevas} que faltaban, {Errores} tramos con error",
            etiqueta, cuenta.ClientId, resultado.TotalLlamadas, resultado.Tramos.Sum(t => t.Insertadas), errores);
        return resultado;
    }

    // Recorre [desde, hasta) por tramos adaptativos sin mover el marcador. Un tramo con error no detiene a los demás.
    // etiqueta = null: manual (cada tramo al .txt); con etiqueta: repaso (solo los tramos con error al .txt).
    private async Task<ResultadoDescarga> DescargarTramosAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, OrigenDescarga origen, string? etiqueta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var tamano = TramosAdaptativos.Inicial(null, o);
        var resultados = new List<ResultadoTramo>();
        for (var actual = desde; actual < hasta && !cancellationToken.IsCancellationRequested;)
        {
            if (Min(actual.AddSeconds(tamano), hasta).AddSeconds(-1) < actual)
            {
                break;
            }

            (var registro, tamano) = await BajarTramoAsync(cuenta, actual, hasta, tamano, origen, moverAvance: false, etiqueta,
                cancellationToken);
            var fin = registro.Hasta;
            if (etiqueta is null || !registro.Exitosa)
            {
                await EscribirReporteAsync(registro, cancellationToken);
            }

            resultados.Add(new ResultadoTramo(actual, fin, registro.Exitosa ? EstadoTramo.Completado : EstadoTramo.Error,
                registro.Paginas, registro.Registros, registro.Insertadas, registro.Actualizadas, registro.Mensaje));

            actual = fin.AddSeconds(1);
            tamano = TramosAdaptativos.Siguiente(tamano, registro.Registros, registro.Paginas, o);
        }

        return new ResultadoDescarga(cuenta.ClientId, resultados);
    }

    /// <summary>
    /// Baja UN tramo desde <paramref name="desde"/> (de <paramref name="tamano"/> segundos, sin pasar de
    /// <paramref name="limite"/>) y lo registra en la bitácora. Si CCC tardó demasiado, falló del lado de CCC (5xx) o
    /// el tramo tuvo demasiadas páginas, reintenta el MISMO inicio con la mitad del tramo (hasta ReintentosReduciendo
    /// veces): un rango más chico es una consulta más ligera para CCC. Cada intento fallido queda en la bitácora.
    /// </summary>
    /// <returns>El registro del último intento y el tamaño que se usó en él.</returns>
    private async Task<(RegistroTramo Registro, int Tamano)> BajarTramoAsync(CuentaCcc cuenta, DateTime desde, DateTime limite,
        int tamano, OrigenDescarga origen, bool moverAvance, string? etiqueta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        for (var intento = 0; ; intento++)
        {
            var hasta = Min(desde.AddSeconds(tamano), limite).AddSeconds(-1);
            var (registro, convieneReducir) = await DescargarTramoAsync(cuenta, desde, hasta, tamano, origen, cancellationToken);
            if (etiqueta is not null)
            {
                registro = registro with { Mensaje = registro.Mensaje is null ? etiqueta : $"{etiqueta}: {registro.Mensaje}" };
            }

            await RegistrarEnBitacoraAsync(registro, moverAvance, cancellationToken);

            if (registro.Exitosa || !convieneReducir || intento >= o.ReintentosReduciendo
                || TramosAdaptativos.Reducido(tamano, o) is not { } menor)
            {
                return (registro, tamano);
            }

            logger.LogInformation("CCC {ClientId}: el tramo de {Tamano} s desde {Desde:HH:mm:ss} falló ({Mensaje}); se reintenta de {Menor} s",
                cuenta.ClientId, tamano, desde, registro.Mensaje, menor);
            tamano = menor;
        }
    }

    // Fallas en las que un rango más chico sí ayuda: timeout (aquí un OperationCanceledException solo llega si nadie
    // canceló), error interno de CCC o demasiadas páginas. Credenciales, red u "ocupado" no se arreglan achicando.
    private static bool ConvieneReducir(Exception ex) =>
        ex is TopePaginasException or OperationCanceledException or CccApiException { StatusCode: >= 500, Ocupado: false };

    private sealed class TopePaginasException(string mensaje) : Exception(mensaje);

    private async Task<(RegistroTramo Registro, bool ConvieneReducir)> DescargarTramoAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, int tamano, OrigenDescarga origen, CancellationToken cancellationToken)
    {
        var inicio = Ahora();
        int paginas = 0, total = 0, insertadas = 0, actualizadas = 0;
        var relojCcc = new System.Diagnostics.Stopwatch();
        var relojBd = new System.Diagnostics.Stopwatch();

        // PIPELINE: mientras la BD guarda la página N, ya se le está pidiendo la N+1 a CCC.
        // El tiempo por página es el mayor de los dos y no la suma (CCC es lo lento: ~2-3 s por página).
        Task<(int Insertadas, int Actualizadas)>? guardando = null;
        var paginaGuardando = 0;
        var donde = "página 1";

        async Task TerminarGuardadoAsync()
        {
            if (guardando is null)
            {
                return;
            }

            donde = $"guardar página {paginaGuardando}";
            relojBd.Start();   // solo cuenta lo que se espera DESPUÉS de que CCC ya respondió
            try
            {
                var (ins, act) = await guardando;
                insertadas += ins;
                actualizadas += act;
            }
            finally
            {
                relojBd.Stop();
                guardando = null;
            }
        }

        try
        {
            while (true)
            {
                donde = $"página {paginas + 1}";
                relojCcc.Start();
                IReadOnlyList<Domain.Llamadas.Llamada> pagina;
                try
                {
                    pagina = await cccClient.BuscarLlamadasAsync(cuenta, desde, hasta, paginas + 1, cancellationToken);
                }
                finally
                {
                    relojCcc.Stop();
                }

                await TerminarGuardadoAsync();   // la página anterior ya debió terminar mientras CCC respondía
                paginas++;

                if (pagina.Count > 0)
                {
                    total += pagina.Count;
                    paginaGuardando = paginas;
                    guardando = llamadas.GuardarAsync(pagina, cancellationToken);
                }

                if (pagina.Count < TamanoPagina)
                {
                    break;   // vino vacía o incompleta: ya no hay más páginas
                }

                if (paginas >= MaxPaginasPorTramo)
                {
                    await TerminarGuardadoAsync();
                    throw new TopePaginasException($"Se alcanzó el máximo de {MaxPaginasPorTramo} páginas en un tramo.");
                }
            }

            await TerminarGuardadoAsync();
            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, true,
                paginas, total, insertadas, actualizadas, inicio, Ahora(), null)
            { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = relojBd.ElapsedMilliseconds }, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var lugar = donde;   // dónde falló de verdad (antes de esperar el guardado pendiente)

            // Si CCC falló con un guardado en curso, se espera a que termine (no se deja una tarea suelta)
            if (guardando is not null)
            {
                try
                {
                    await TerminarGuardadoAsync();
                }
                catch (Exception exGuardado) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exGuardado, "También falló el guardado de la página {Pagina}", paginaGuardando);
                }
            }

            // Lo guardado antes del error se queda (no duplica al repetir el tramo)
            logger.LogWarning(ex, "Falló la descarga CCC {ClientId} de {Desde:yyyy-MM-dd HH:mm:ss}-{Hasta:HH:mm:ss} ({Donde})",
                cuenta.ClientId, desde, hasta, lugar);

            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, false,
                paginas, total, insertadas, actualizadas, inicio, Ahora(), Explicar(ex, lugar))
            { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = relojBd.ElapsedMilliseconds }, ConvieneReducir(ex));
        }
    }

    // Si la bitácora falla no se pierde el resultado; se avisa en el log. Si era automática, como el avance no se
    // movió, el mismo tramo se vuelve a pedir y no duplica.
    private async Task RegistrarEnBitacoraAsync(RegistroTramo registro, bool moverAvance, CancellationToken cancellationToken)
    {
        try
        {
            await bitacora.RegistrarAsync(registro, moverAvance, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "No se pudo registrar en la bitácora el tramo CCC {ClientId} {Desde:yyyy-MM-dd HH:mm:ss}",
                registro.ClientId, registro.Desde);
            if (moverAvance)
            {
                throw new ErrorBaseDatosException("no se pudo registrar el avance en la bitácora", ex);
            }
        }
    }

    private async Task EscribirReporteAsync(RegistroTramo registro, CancellationToken cancellationToken)
    {
        try
        {
            await reporte.EscribirTramoAsync(registro, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "No se pudo escribir el reporte .txt de la cuenta CCC {ClientId}", registro.ClientId);
        }
    }

    /// <summary>El porqué de la falla, en palabras claras para el reporte y el correo.</summary>
    public static string Explicar(Exception ex, string donde)
    {
        var motivo = ex switch
        {
            TopePaginasException =>
                ex.Message,
            CccApiException { Ocupado: true } =>
                "CCC siguió ocupado con otra consulta (code 101) después de todos los reintentos",
            CccApiException { StatusCode: 401 } =>
                "CCC rechazó las credenciales (HTTP 401): revisar la SecretKey de la cuenta",
            CccApiException c when c.StatusCode >= 500 =>
                $"Falla del lado de CCC (HTTP {c.StatusCode})",
            CccApiException c =>
                $"CCC rechazó la consulta (HTTP {c.StatusCode}): {c.Message}",
            HttpRequestException =>
                "Intermitencia de red o la API de CCC no respondió (conexión cortada o rechazada)",
            OperationCanceledException =>
                "CCC tardó demasiado en responder (timeout)",
            ErrorBaseDatosException e =>
                $"No se pudo guardar en la base de datos: {e.Message}",
            JsonException =>
                "CCC regresó una respuesta con formato inesperado",
            _ =>
                $"Error inesperado: {ex.Message}",
        };

        var texto = $"{motivo} ({donde})";
        return texto.Length <= 1000 ? texto : texto[..1000];
    }

    private DateTime Ahora() => timeProvider.GetLocalNow().DateTime;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    internal static DateTime TruncarASegundo(DateTime fecha) =>
        new(fecha.Ticks - fecha.Ticks % TimeSpan.TicksPerSecond, fecha.Kind);
}
