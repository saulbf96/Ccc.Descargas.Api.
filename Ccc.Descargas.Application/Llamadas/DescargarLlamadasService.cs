using System.Text.Json;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Application.Llamadas;

// descarga las llamadas de una cuenta por tramos y las guarda
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
    public const int TamanoPagina = 1000;   // máximo de CCC por página
    public const int MaxPaginasPorTramo = 500;   // tope de seguridad
    public static readonly TimeSpan RangoMaximoManual = TimeSpan.FromHours(24);

    public const string EtiquetaRecuperacion = "Recuperación";

    // frente en vivo: avanza desde el marcador hasta el límite
    // si va más atrasado que UmbralSaltoMinutos salta al límite y lo de en medio queda como hueco por recuperar
    public async Task<ResultadoTurno> AvanzarAsync(CuentaCcc cuenta, DateTime limite, CancellationToken cancellationToken)
    {
        var o = options.Value;
        limite = TruncarASegundo(limite);   // la BD guarda segundos enteros
        var avance = await bitacora.ObtenerAvanceAsync(cuenta.ClientId, TipoDescarga.Llamadas, cancellationToken);
        // primera vez: desde las 00:00 de hoy, después desde el marcador
        var desde = avance?.DescargadoHasta ?? limite.Date;
        var tamano = TramosAdaptativos.Inicial(avance?.TamanoTramoSegundos, o);

        DateTime? saltoDesde = null;
        if (limite - desde > TimeSpan.FromMinutes(o.UmbralSaltoMinutos)
            && await bitacora.SaltarAsync(cuenta.ClientId, desde, limite, tamano, cancellationToken))
        {
            logger.LogInformation("CCC {ClientId}: iba atrasada desde {Desde:dd/MM HH:mm:ss}; el frente en vivo salta a {Limite:dd/MM HH:mm:ss} y lo de en medio se recupera aparte",
                cuenta.ClientId, desde, limite);
            saltoDesde = desde;
            desde = limite;
        }

        var turno = await RecorrerAsync(cuenta, desde, limite, tamano, finHueco: null, etiqueta: null, cancellationToken);
        return turno with { SaltoDesde = saltoDesde };
    }

    // frente de recuperación: baja el hueco más viejo, null si no hay nada que recuperar
    public async Task<ResultadoRecuperacion?> RecuperarAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var huecos = await bitacora.ObtenerHuecosAsync(cuenta.ClientId, cancellationToken);
        if (huecos.Count == 0)
        {
            return null;
        }

        var hueco = huecos[0];
        var tamano = TramosAdaptativos.Inicial(hueco.TamanoTramoSegundos, options.Value);
        var turno = await RecorrerAsync(cuenta, hueco.Desde, hueco.Hasta, tamano, finHueco: hueco.Hasta,
            etiqueta: EtiquetaRecuperacion, cancellationToken);

        // lo que falta de todos los huecos después de este turno
        var pendiente = huecos.Aggregate(TimeSpan.Zero, (s, h) => s + (h.Hasta - h.Desde)) - (turno.DescargadoHasta - hueco.Desde);
        return new ResultadoRecuperacion(turno, huecos.Count, pendiente < TimeSpan.Zero ? TimeSpan.Zero : pendiente);
    }

    // recorre [desde, limite) por tramos durante un turno; con finHueco avanza ese hueco en lugar del marcador
    private async Task<ResultadoTurno> RecorrerAsync(CuentaCcc cuenta, DateTime desde, DateTime limite, int tamano,
        DateTime? finHueco, string? etiqueta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var finTurno = Ahora().AddMinutes(o.MinutosPorTurno);
        var inicioTurno = desde;

        int tramos = 0, total = 0, paginas = 0, insertadas = 0, actualizadas = 0;
        var inicio = Ahora();
        RegistroTramo? error = null;
        var confirmado = desde;   // hasta dónde ya está guardado y registrado

        // el tramo anterior se sigue guardando y registrando mientras se pide el siguiente a CCC
        Task<RegistroTramo>? anterior = null;

        // espera que el tramo anterior quede guardado y registrado; así el marcador avanza en orden
        async Task<bool> CerrarAnteriorAsync()
        {
            if (anterior is not { } cierre)
            {
                return error is null;
            }

            anterior = null;
            var registro = await cierre;
            if (!registro.Exitosa)
            {
                error ??= registro;
                await EscribirReporteAsync(registro, cancellationToken);
                return false;
            }

            tramos++;
            total += registro.Registros;
            paginas += registro.Paginas;
            insertadas += registro.Insertadas;
            actualizadas += registro.Actualizadas;
            confirmado = registro.Hasta.AddSeconds(1);
            return true;
        }

        while (desde < limite && Ahora() < finTurno && !cancellationToken.IsCancellationRequested)
        {
            if (Min(desde.AddSeconds(tamano), limite).AddSeconds(-1) < desde)
            {
                break;   // falta menos de un segundo
            }

            var (registro, pendiente, usado) = await BajarTramoAsync(cuenta, desde, limite, tamano, OrigenDescarga.Automatica,
                moverAvance: true, etiqueta, diferirGuardado: true, antesDeRegistrar: CerrarAnteriorAsync, cancellationToken, finHueco);

            if (pendiente is null)
            {
                // falló en CCC (ya quedó en la bitácora)
                await CerrarAnteriorAsync();
                if (error is null)
                {
                    error = registro;
                    await EscribirReporteAsync(registro, cancellationToken);
                }

                break;
            }

            if (!await CerrarAnteriorAsync())
            {
                // falló el guardado anterior: este se guarda pero no se registra, el siguiente turno repite desde el error
                await DescartarAsync(pendiente);
                break;
            }

            // guardar y registrar en la bitácora corre aparte mientras se pide el siguiente tramo
            anterior = CerrarAsync(pendiente, cancellationToken);
            desde = registro.Hasta.AddSeconds(1);
            tamano = TramosAdaptativos.Siguiente(usado, registro.Registros, registro.Paginas, o);
        }

        await CerrarAnteriorAsync();

        if (tramos > 0)
        {
            // una línea por turno en el .txt
            await EscribirReporteAsync(new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, inicioTurno,
                confirmado.AddSeconds(-1), tamano, OrigenDescarga.Automatica, true, paginas, total, insertadas, actualizadas,
                inicio, Ahora(), etiqueta is null ? $"{tramos} tramo(s)" : $"{etiqueta}: {tramos} tramo(s)"), cancellationToken);
        }

        return new ResultadoTurno(cuenta.ClientId, tramos, total, confirmado, limite, error);
    }

    // tramo ya bajado de CCC con su última página guardándose
    private sealed record TramoPendiente(RegistroTramo Registro, Task<(int Insertadas, int Actualizadas)> Guardando, int Pagina);

    // espera el guardado pendiente y registra el tramo con el avance
    private async Task<RegistroTramo> CerrarAsync(TramoPendiente pendiente, CancellationToken cancellationToken)
    {
        var registro = pendiente.Registro;
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (ins, act) = await pendiente.Guardando;
            // MS_BD: lo que se tuvo que esperar a la BD, sin contar lo que se guardó mientras se pedía a CCC
            registro = registro with
            {
                Insertadas = registro.Insertadas + ins,
                Actualizadas = registro.Actualizadas + act,
                Fin = Ahora(),
                MsBd = (registro.MsBd ?? 0) + reloj.ElapsedMilliseconds,
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falló el guardado CCC {ClientId} de {Desde:yyyy-MM-dd HH:mm:ss}-{Hasta:HH:mm:ss} (página {Pagina})",
                registro.ClientId, registro.Desde, registro.Hasta, pendiente.Pagina);
            var motivo = Explicar(ex, $"guardar página {pendiente.Pagina}");
            registro = registro with
            {
                Exitosa = false,
                Fin = Ahora(),
                Mensaje = registro.Mensaje is null ? motivo : $"{registro.Mensaje}: {motivo}",   // conserva la etiqueta
            };
        }

        await RegistrarEnBitacoraAsync(registro, moverAvance: true, cancellationToken);
        return registro;
    }

    // espera un guardado sin registrarlo
    private async Task DescartarAsync(TramoPendiente pendiente)
    {
        try
        {
            await pendiente.Guardando;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "También falló el guardado CCC {ClientId} desde {Desde:yyyy-MM-dd HH:mm:ss}",
                pendiente.Registro.ClientId, pendiente.Registro.Desde);
        }
    }

    // descarga manual de un rango, no mueve el marcador
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

    // repaso del día: vuelve a pedir solo lo que se bajó en vivo (CCC pudo actualizarlo después)
    // lo que se bajó horas después de pasar ya estaba completo; null si el día todavía no está todo descargado
    public async Task<ResultadoDescarga?> RepasarDiaAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken)
    {
        var inicioDia = dia.ToDateTime(TimeOnly.MinValue);
        var finDia = inicioDia.AddDays(1);

        var avance = await bitacora.ObtenerAvanceAsync(cuenta.ClientId, TipoDescarga.Llamadas, cancellationToken);
        if (avance is null || avance.DescargadoHasta < finDia)
        {
            return null;   // el frente en vivo no ha pasado la medianoche
        }

        var huecos = await bitacora.ObtenerHuecosAsync(cuenta.ClientId, cancellationToken);
        if (huecos.Any(h => h.Desde < finDia && h.Hasta > inicioDia))
        {
            return null;   // falta recuperar parte del día
        }

        var rangos = await bitacora.ObtenerTramosEnVivoAsync(cuenta.ClientId, inicioDia, finDia, options.Value.VentanaRepasoMinutos,
            cancellationToken);
        if (rangos.Count == 0)
        {
            logger.LogInformation("CCC {ClientId}: el {Dia:dd/MM} se bajó completo después de pasar; no necesita repaso", cuenta.ClientId, dia);
            return new ResultadoDescarga(cuenta.ClientId, []);
        }

        var inicio = Ahora();
        var etiqueta = $"Repaso del {dia:dd/MM}";
        var tramos = new List<ResultadoTramo>();
        foreach (var rango in rangos)
        {
            var parte = await DescargarTramosAsync(cuenta, rango.Desde, rango.Hasta.AddSeconds(1), OrigenDescarga.Automatica,
                etiqueta, cancellationToken);
            tramos.AddRange(parte.Tramos);
        }

        var resultado = new ResultadoDescarga(cuenta.ClientId, tramos);

        // una línea por todo el repaso
        var errores = resultado.TramosConError;
        await EscribirReporteAsync(new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, rangos[0].Desde,
            rangos[^1].Hasta, null, OrigenDescarga.Automatica, errores == 0, resultado.Tramos.Sum(t => t.Paginas),
            resultado.TotalLlamadas, resultado.Tramos.Sum(t => t.Insertadas), resultado.Tramos.Sum(t => t.Actualizadas),
            inicio, Ahora(), errores == 0 ? $"{etiqueta} ({rangos.Count} rango(s) bajados en vivo)"
                : $"{etiqueta}: {errores} tramo(s) con error; se repite el repaso"), cancellationToken);

        logger.LogInformation("{Etiqueta} CCC {ClientId}: {Llamadas} llamadas, {Nuevas} que faltaban, {Errores} tramos con error",
            etiqueta, cuenta.ClientId, resultado.TotalLlamadas, resultado.Tramos.Sum(t => t.Insertadas), errores);
        return resultado;
    }

    // recorre el rango por tramos sin mover el marcador
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

            (var registro, _, tamano) = await BajarTramoAsync(cuenta, actual, hasta, tamano, origen, moverAvance: false, etiqueta,
                diferirGuardado: false, antesDeRegistrar: null, cancellationToken);
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

    // baja un tramo; si no cabe en una página, o hay timeout o 5xx, pide el mismo inicio a la mitad
    // diferirGuardado: si sale bien no espera la última página ni lo registra, lo regresa como pendiente
    // antesDeRegistrar: se espera antes de escribir en la bitácora (cierra el tramo anterior)
    // finHueco: tramo de recuperación (avanza ese hueco en lugar del marcador)
    private async Task<(RegistroTramo Registro, TramoPendiente? Pendiente, int Tamano)> BajarTramoAsync(CuentaCcc cuenta,
        DateTime desde, DateTime limite, int tamano, OrigenDescarga origen, bool moverAvance, string? etiqueta,
        bool diferirGuardado, Func<Task<bool>>? antesDeRegistrar, CancellationToken cancellationToken, DateTime? finHueco = null)
    {
        var o = options.Value;
        long msCortes = 0;
        for (var intento = 0; ; )
        {
            var hasta = Min(desde.AddSeconds(tamano), limite).AddSeconds(-1);
            // el límite puede dejar el tramo más corto
            var segundos = (int)(hasta - desde).TotalSeconds + 1;
            var mitad = TramosAdaptativos.Reducido(segundos, o);
            var (registro, convieneReducir, muyGrande, guardando, pagina) = await DescargarTramoAsync(cuenta, desde, hasta, tamano,
                origen, cortarSiNoCabe: mitad is not null, diferirGuardado, cancellationToken);

            if (muyGrande && mitad is { } corto)
            {
                // no se guardó nada ni cuenta como error
                logger.LogInformation("CCC {ClientId}: el tramo de {Segundos} s desde {Desde:dd/MM HH:mm:ss} no cabe en una página; se pide de {Corto} s",
                    cuenta.ClientId, segundos, desde, corto);
                msCortes += registro.MsCcc ?? 0;
                tamano = corto;
                continue;
            }

            if (msCortes > 0)
            {
                registro = registro with { MsCcc = (registro.MsCcc ?? 0) + msCortes };   // el tiempo de los cortes también cuenta
            }

            registro = registro with { FinHueco = finHueco };
            if (etiqueta is not null)
            {
                registro = registro with { Mensaje = registro.Mensaje is null ? etiqueta : $"{etiqueta}: {registro.Mensaje}" };
            }

            if (guardando is not null)
            {
                return (registro, new TramoPendiente(registro, guardando, pagina), tamano);
            }

            if (antesDeRegistrar is not null)
            {
                await antesDeRegistrar();
            }

            await RegistrarEnBitacoraAsync(registro, moverAvance, cancellationToken);

            if (registro.Exitosa || !convieneReducir || intento >= o.ReintentosReduciendo || mitad is not { } menor)
            {
                return (registro, null, tamano);
            }

            logger.LogInformation("CCC {ClientId}: el tramo de {Tamano} s desde {Desde:HH:mm:ss} falló ({Mensaje}); se reintenta de {Menor} s",
                cuenta.ClientId, tamano, desde, registro.Mensaje, menor);
            tamano = menor;
            intento++;
        }
    }

    // errores donde sí ayuda achicar el tramo
    private static bool ConvieneReducir(Exception ex) =>
        ex is TopePaginasException or OperationCanceledException or CccApiException { StatusCode: >= 500, Ocupado: false };

    private sealed class TopePaginasException(string mensaje) : Exception(mensaje);

    // cortarSiNoCabe: si la primera página sale llena no sigue paginando (MuyGrande)
    // diferirGuardado: si sale bien regresa la última página guardándose (Guardando) sin esperarla
    private async Task<(RegistroTramo Registro, bool ConvieneReducir, bool MuyGrande, Task<(int, int)>? Guardando, int Pagina)>
        DescargarTramoAsync(CuentaCcc cuenta, DateTime desde, DateTime hasta, int tamano, OrigenDescarga origen, bool cortarSiNoCabe,
        bool diferirGuardado, CancellationToken cancellationToken)
    {
        var inicio = Ahora();
        int paginas = 0, total = 0, insertadas = 0, actualizadas = 0;
        var relojCcc = new System.Diagnostics.Stopwatch();
        var relojBd = new System.Diagnostics.Stopwatch();

        // mientras se guarda una página se pide la siguiente
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
            relojBd.Start();
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

                await TerminarGuardadoAsync();
                paginas++;

                // en CCC un rango grande tarda mucho más por página: conviene pedirlo más chico
                if (cortarSiNoCabe && paginas == 1 && pagina.Count >= TamanoPagina)
                {
                    return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, false,
                        paginas, pagina.Count, 0, 0, inicio, Ahora(), "El tramo no cabe en una página")
                    { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = 0 }, false, true, null, 0);
                }

                if (pagina.Count > 0)
                {
                    total += pagina.Count;
                    paginaGuardando = paginas;
                    guardando = llamadas.GuardarAsync(pagina, cancellationToken);
                }

                if (pagina.Count < TamanoPagina)
                {
                    break;   // ya no hay más páginas
                }

                if (paginas >= MaxPaginasPorTramo)
                {
                    await TerminarGuardadoAsync();
                    throw new TopePaginasException($"Se alcanzó el máximo de {MaxPaginasPorTramo} páginas en un tramo.");
                }
            }

            if (diferirGuardado)
            {
                // la última página se termina de guardar mientras se pide el siguiente tramo
                var pendiente = guardando ?? Task.FromResult((0, 0));
                return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, true,
                    paginas, total, insertadas, actualizadas, inicio, Ahora(), null)
                { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = relojBd.ElapsedMilliseconds }, false, false, pendiente, paginaGuardando);
            }

            await TerminarGuardadoAsync();
            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, true,
                paginas, total, insertadas, actualizadas, inicio, Ahora(), null)
            { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = relojBd.ElapsedMilliseconds }, false, false, null, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var lugar = donde;

            // esperar el guardado pendiente
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

            // lo guardado antes del error se queda
            logger.LogWarning(ex, "Falló la descarga CCC {ClientId} de {Desde:yyyy-MM-dd HH:mm:ss}-{Hasta:HH:mm:ss} ({Donde})",
                cuenta.ClientId, desde, hasta, lugar);

            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.Llamadas, desde, hasta, tamano, origen, false,
                paginas, total, insertadas, actualizadas, inicio, Ahora(), Explicar(ex, lugar))
            { MsCcc = relojCcc.ElapsedMilliseconds, MsBd = relojBd.ElapsedMilliseconds }, ConvieneReducir(ex), false, null, 0);
        }
    }

    // si la bitácora falla solo se avisa en el log
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

    // mensaje de error para reporte y correo
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
