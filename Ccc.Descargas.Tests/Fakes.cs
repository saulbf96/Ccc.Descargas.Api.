using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Cuentas;
using Ccc.Descargas.Domain.Llamadas;

namespace Ccc.Descargas.Tests;

// fakes para probar sin red ni SQL
internal static class Datos
{
    public static readonly CuentaCcc Consu = new("2812", "CCC1 - CONSU 2812", "api.ccc.uno");
    public static readonly CuentaCcc Isb = new("200929", "CCC2 - ISB CDMX 3-200", "app2.ccc.uno");

    public static Llamada Llamada(long callId, int clientId = 2812) => new()
    {
        ClientId = clientId,
        CallId = callId,
        CallDirection = "outbound",
        CallerIdNumber = "5500000000",
        CalledNumber = "5511111111",
        AnswerState = "ANSWERED",
        DurationSecs = 30,
        StartDatetime = new DateTime(2026, 10, 30, 10, 5, 0),
    };

    public static IReadOnlyList<Llamada> Pagina(int cantidad, long desde = 1) =>
        Enumerable.Range(0, cantidad).Select(i => Llamada(desde + i)).ToList();
}

internal sealed class CuentasFake(params CuentaCcc[] cuentas) : ICuentaCccRepository
{
    public IReadOnlyList<CuentaCcc> ObtenerTodas() => cuentas;

    public CuentaCcc? ObtenerPorId(string clientId) => cuentas.FirstOrDefault(c => c.ClientId == clientId);
}

// CCC fake
internal sealed class CccFake(params Func<IReadOnlyList<Llamada>>[] respuestas) : ICccClient
{
    private int siguiente;

    public int Consultas => siguiente;

    // rangos pedidos
    public List<(DateTime Desde, DateTime Hasta, int Pagina)> Pedidos { get; } = [];

    public IReadOnlyList<TiempoAgente> Tiempos { get; init; } = [];

    public Func<long, EstatusAgente?> Estatus { get; init; } = _ => null;

    public Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Llamada>> BuscarLlamadasAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, int pagina, CancellationToken cancellationToken)
    {
        Pedidos.Add((desde, hasta, pagina));
        // Sin más respuestas preparadas = ya no hay llamadas
        return Task.FromResult(siguiente < respuestas.Length ? respuestas[siguiente++]() : (IReadOnlyList<Llamada>)[]);
    }

    public Task<IReadOnlyList<TiempoAgente>> ObtenerTiemposAgentesAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken) =>
        Task.FromResult(Tiempos);

    public Task<EstatusAgente?> ObtenerEstatusAgenteAsync(CuentaCcc cuenta, long userId, DateTime fechaFoto, CancellationToken cancellationToken) =>
        Task.FromResult(Estatus(userId) is { } e ? e with { FechaFoto = fechaFoto } : null);
}

internal sealed class LlamadasFake : ILlamadaRepository
{
    public List<Llamada> Guardadas { get; } = [];

    public Task<(int Insertadas, int Actualizadas)> GuardarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken cancellationToken)
    {
        Guardadas.AddRange(llamadas);
        return Task.FromResult((llamadas.Count, 0));
    }
}

// guardado que termina cuando la prueba lo indica
internal sealed class GuardadoControlado : ILlamadaRepository
{
    private readonly List<TaskCompletionSource<(int, int)>> pendientes = [];
    private readonly List<int> cantidades = [];
    private readonly Lock candado = new();
    private bool terminarAlLlegar;

    public Task<(int Insertadas, int Actualizadas)> GuardarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (candado)
        {
            pendientes.Add(tcs);
            cantidades.Add(llamadas.Count);
            if (terminarAlLlegar)
            {
                tcs.TrySetResult((llamadas.Count, 0));
            }
        }

        return tcs.Task;
    }

    public async Task EsperarLlamadasAsync(int cuantas)
    {
        for (var i = 0; i < 200; i++)
        {
            lock (candado)
            {
                if (pendientes.Count >= cuantas)
                {
                    return;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("no llegaron los guardados esperados");
    }

    public void Fallar(int indice, Exception ex)
    {
        lock (candado)
        {
            pendientes[indice].TrySetException(ex);
        }
    }

    // termina los pendientes y los que lleguen después
    public void TerminarTodos()
    {
        lock (candado)
        {
            terminarAlLlegar = true;
            for (var i = 0; i < pendientes.Count; i++)
            {
                pendientes[i].TrySetResult((cantidades[i], 0));
            }
        }
    }
}

internal sealed class AgentesFake : IAgenteRepository
{
    public List<EstatusAgente> Fotos { get; } = [];
    public List<TiempoAgente> Tiempos { get; } = [];

    public Task<int> GuardarEstatusAsync(string clientId, IReadOnlyList<EstatusAgente> fotos, CancellationToken cancellationToken)
    {
        Fotos.AddRange(fotos);
        return Task.FromResult(fotos.Count);
    }

    public Task<(int Insertadas, int Actualizadas)> GuardarTiemposAsync(string clientId, IReadOnlyList<TiempoAgente> tiempos, CancellationToken cancellationToken)
    {
        Tiempos.AddRange(tiempos);
        return Task.FromResult((tiempos.Count, 0));
    }
}

// bitácora en memoria
internal sealed class BitacoraFake(bool falla = false) : IBitacoraDescargas
{
    public List<(RegistroTramo Registro, bool MoverAvance)> Registros { get; } = [];

    public Dictionary<(string, TipoDescarga), AvanceDescarga> Avance { get; } = [];

    public Task RegistrarAsync(RegistroTramo registro, bool moverAvance, CancellationToken cancellationToken)
    {
        if (falla)
        {
            throw new InvalidOperationException("BD caída");
        }

        Registros.Add((registro, moverAvance));
        if (moverAvance && registro.FinHueco is { } finHueco)
        {
            // recuperación: avanza el hueco y lo borra al terminarlo
            var i = Huecos.FindIndex(h => h.ClientId == registro.ClientId && h.Hueco.Hasta == finHueco);
            if (registro.Exitosa && i >= 0)
            {
                var nuevo = Huecos[i].Hueco with { Desde = registro.Hasta.AddSeconds(1), TamanoTramoSegundos = registro.TamanoTramoSegundos };
                if (nuevo.Desde >= nuevo.Hasta)
                {
                    Huecos.RemoveAt(i);
                }
                else
                {
                    Huecos[i] = (registro.ClientId, nuevo);
                }
            }
        }
        else if (moverAvance)
        {
            var llave = (registro.ClientId, registro.Tipo);
            Avance.TryGetValue(llave, out var actual);
            Avance[llave] = registro.Exitosa
                ? new AvanceDescarga(registro.Hasta.AddSeconds(1), registro.TamanoTramoSegundos, 0, null, null)
                : new AvanceDescarga(actual?.DescargadoHasta ?? registro.Desde, actual?.TamanoTramoSegundos,
                    (actual?.ErroresSeguidos ?? 0) + 1, registro.Mensaje, DateTime.Now);
        }

        return Task.CompletedTask;
    }

    public Task<AvanceDescarga?> ObtenerAvanceAsync(string clientId, TipoDescarga tipo, CancellationToken cancellationToken) =>
        Task.FromResult(Avance.TryGetValue((clientId, tipo), out var a) ? a : null);

    public Task<ResumenCuenta> ObtenerResumenAsync(string clientId, DateTime desde, DateTime hasta, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public List<(string ClientId, HuecoDescarga Hueco)> Huecos { get; } = [];

    // misma regla que @OPCION 7
    public Task<bool> SaltarAsync(string clientId, DateTime desde, DateTime hasta, int? tamanoTramoSegundos, CancellationToken cancellationToken)
    {
        var llave = (clientId, TipoDescarga.Llamadas);
        var existe = Avance.TryGetValue(llave, out var actual);
        if (hasta <= desde || (existe && actual!.DescargadoHasta != desde))
        {
            return Task.FromResult(false);
        }

        Avance[llave] = existe ? actual! with { DescargadoHasta = hasta } : new AvanceDescarga(hasta, tamanoTramoSegundos, 0, null, null);
        var i = Huecos.FindIndex(h => h.ClientId == clientId && h.Hueco.Hasta == desde);
        if (i >= 0)
        {
            Huecos[i] = (clientId, Huecos[i].Hueco with { Hasta = hasta });   // se junta con el anterior
        }
        else
        {
            Huecos.Add((clientId, new HuecoDescarga(desde, hasta, tamanoTramoSegundos)));
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<HuecoDescarga>> ObtenerHuecosAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HuecoDescarga>>(Huecos.Where(h => h.ClientId == clientId).Select(h => h.Hueco)
            .OrderBy(h => h.Desde).ToList());

    // misma regla que @OPCION 9
    public Task<IReadOnlyList<RangoTramos>> ObtenerTramosEnVivoAsync(string clientId, DateTime desde, DateTime hasta, int ventanaMinutos,
        CancellationToken cancellationToken)
    {
        var tramos = Registros.Select(r => r.Registro)
            .Where(r => r.ClientId == clientId && r.Tipo == TipoDescarga.Llamadas && r.Exitosa && r.Origen == OrigenDescarga.Automatica
                        && r.Desde >= desde && r.Desde < hasta && !(r.Mensaje?.StartsWith("Repaso", StringComparison.Ordinal) ?? false)
                        && (r.Fin - r.Hasta).TotalMinutes < ventanaMinutos)
            .OrderBy(r => r.Desde).ThenBy(r => r.Hasta);
        var rangos = new List<RangoTramos>();
        foreach (var t in tramos)
        {
            if (rangos.Count > 0 && t.Desde.AddSeconds(-1) <= rangos[^1].Hasta)
            {
                rangos[^1] = rangos[^1] with { Hasta = t.Hasta > rangos[^1].Hasta ? t.Hasta : rangos[^1].Hasta };
            }
            else
            {
                rangos.Add(new RangoTramos(t.Desde, t.Hasta));
            }
        }

        return Task.FromResult<IReadOnlyList<RangoTramos>>(rangos.Select(r => r.Hasta >= hasta ? r with { Hasta = hasta.AddSeconds(-1) } : r).ToList());
    }
}

internal sealed class ReporteFake : IReporteDescargas
{
    public List<RegistroTramo> Tramos { get; } = [];

    public Task EscribirTramoAsync(RegistroTramo registro, CancellationToken cancellationToken)
    {
        Tramos.Add(registro);
        return Task.CompletedTask;
    }

    public Task EscribirAvisoAsync(string? clientId, string mensaje, CancellationToken cancellationToken) => Task.CompletedTask;
}

// reloj fijo
internal sealed class RelojFijo(DateTime ahora) : TimeProvider
{
    public DateTime Ahora { get; set; } = ahora;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Ahora, DateTimeKind.Utc));

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
