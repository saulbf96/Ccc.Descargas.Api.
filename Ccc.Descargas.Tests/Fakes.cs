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
        if (moverAvance)
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
