using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Application.Agentes;

// agentes de una cuenta: foto de estatus, tiempos del día y cierre de días
public sealed class DescargarAgentesService(
    ICuentaCccRepository cuentas,
    ICccClient cccClient,
    IAgenteRepository agentes,
    IBitacoraDescargas bitacora,
    IReporteDescargas reporte,
    IOptions<DescargaOptions> options,
    TimeProvider timeProvider,
    ILogger<DescargarAgentesService> logger)
{
    public CuentaCcc? ObtenerCuenta(string clientId) => cuentas.ObtenerPorId(clientId);

    // tiempos del día + foto de estatus
    public async Task<(RegistroTramo Tiempos, RegistroTramo? Estatus)> TomarFotoAsync(
        CuentaCcc cuenta, OrigenDescarga origen, CancellationToken cancellationToken)
    {
        var ahora = DescargarLlamadasService.TruncarASegundo(Ahora());
        var hoy = DateOnly.FromDateTime(ahora);

        var (registroTiempos, lista) = await DescargarTiemposAsync(cuenta, hoy, origen, ahora, cancellationToken);
        await bitacora.RegistrarAsync(registroTiempos, moverAvance: false, cancellationToken);
        if (!registroTiempos.Exitosa)
        {
            await EscribirReporteAsync(registroTiempos, cancellationToken);
            return (registroTiempos, null);
        }

        var inicio = Ahora();
        var fotos = new List<EstatusAgente>();
        var errores = 0;
        string? ultimoError = null;
        foreach (var agente in lista)
        {
            try
            {
                var foto = await cccClient.ObtenerEstatusAgenteAsync(cuenta, agente.AgentId, ahora, cancellationToken);
                if (foto is not null && !string.IsNullOrWhiteSpace(foto.AgentStatus))
                {
                    fotos.Add(foto with { AgentUsername = agente.AgentUsername, AgentName = agente.AgentName });
                }


                
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // si un agente falla siguen los demás
                errores++;
                ultimoError = DescargarLlamadasService.Explicar(ex, $"agente {agente.AgentId}");
                logger.LogWarning(ex, "Falló Agent/getStatus CCC {ClientId} agente {UserId}", cuenta.ClientId, agente.AgentId);
            }
        }

        RegistroTramo registroEstatus;
        try
        {
            var insertadas = fotos.Count > 0 ? await agentes.GuardarEstatusAsync(cuenta.ClientId, fotos, cancellationToken) : 0;
            var mensaje = errores > 0 ? $"{errores} de {lista.Count} agente(s) sin foto. Último: {ultimoError}" : null;
            registroEstatus = new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.EstatusAgentes, ahora, ahora, null, origen,
                errores == 0, lista.Count, fotos.Count, insertadas, 0, inicio, Ahora(), Recortar(mensaje));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            registroEstatus = new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.EstatusAgentes, ahora, ahora, null, origen,
                false, lista.Count, fotos.Count, 0, 0, inicio, Ahora(), DescargarLlamadasService.Explicar(ex, "guardar fotos"));
        }

        await bitacora.RegistrarAsync(registroEstatus, moverAvance: false, cancellationToken);
        await EscribirReporteAsync(registroEstatus, cancellationToken);
        return (registroTiempos, registroEstatus);
    }

    // siguiente día por cerrar, null si está al día
    public async Task<DateOnly?> DiaPorCerrarAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var ahora = Ahora();
        if (ahora.TimeOfDay < o.HoraCierreDia)
        {
            return null;   // todavía no es hora de cerrar ayer
        }

        var hoy = DateOnly.FromDateTime(ahora);
        var ayer = hoy.AddDays(-1);
        var avance = await bitacora.ObtenerAvanceAsync(cuenta.ClientId, TipoDescarga.TiemposAgentes, cancellationToken);
        if (avance is null)
        {
            // primera vez: no cierra días anteriores, el marcador queda en hoy
            var inicioHoy = hoy.ToDateTime(TimeOnly.MinValue);
            await bitacora.RegistrarAsync(new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.TiemposAgentes,
                inicioHoy.AddSeconds(-1), inicioHoy.AddSeconds(-1), null, OrigenDescarga.Automatica, true, 0, 0, 0, 0, ahora, ahora,
                $"Inicio: los tiempos se cierran a partir del {hoy:dd/MM}"), moverAvance: true, cancellationToken);
            return null;
        }

        var siguiente = DateOnly.FromDateTime(avance.DescargadoHasta);
        var masAntiguo = ayer.AddDays(-(o.DiasCierreAtras - 1));
        if (siguiente < masAntiguo)
        {
            siguiente = masAntiguo;
        }

        return siguiente <= ayer ? siguiente : null;
    }

    // tiempos del día completo, si sale bien pasa al día siguiente
    public async Task<RegistroTramo> CerrarDiaAsync(CuentaCcc cuenta, DateOnly dia, OrigenDescarga origen, CancellationToken cancellationToken)
    {
        var desde = dia.ToDateTime(TimeOnly.MinValue);
        var (registro, _) = await DescargarTiemposAsync(cuenta, dia, origen, desde, cancellationToken);
        registro = registro with { Hasta = desde.AddDays(1).AddSeconds(-1) };
        await bitacora.RegistrarAsync(registro, moverAvance: origen == OrigenDescarga.Automatica, cancellationToken);
        await EscribirReporteAsync(registro, cancellationToken);
        return registro;
    }

    private async Task<(RegistroTramo Registro, IReadOnlyList<TiempoAgente> Lista)> DescargarTiemposAsync(
        CuentaCcc cuenta, DateOnly dia, OrigenDescarga origen, DateTime marca, CancellationToken cancellationToken)
    {
        var inicio = Ahora();
        try
        {
            var lista = await cccClient.ObtenerTiemposAgentesAsync(cuenta, dia, cancellationToken);
            var (ins, act) = lista.Count > 0 ? await agentes.GuardarTiemposAsync(cuenta.ClientId, lista, cancellationToken) : (0, 0);
            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.TiemposAgentes, marca, marca, null, origen, true,
                1, lista.Count, ins, act, inicio, Ahora(), null), lista);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falló Agent/getTimes CCC {ClientId} del {Dia}", cuenta.ClientId, dia);
            return (new RegistroTramo(cuenta.ClientId, cuenta.Nombre, TipoDescarga.TiemposAgentes, marca, marca, null, origen, false,
                1, 0, 0, 0, inicio, Ahora(), DescargarLlamadasService.Explicar(ex, $"tiempos del {dia:dd/MM}")), []);
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

    private static string? Recortar(string? texto) => texto is { Length: > 1000 } ? texto[..1000] : texto;

    private DateTime Ahora() => timeProvider.GetLocalNow().DateTime;
}
