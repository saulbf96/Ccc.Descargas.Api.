using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Cuentas;
using Ccc.Descargas.Domain.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

// cliente de CCC, lanza CccApiException si no responde 200 (menos Ping)
public interface ICccClient
{
    Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken);

    // una página de CallHistory/Search
    Task<IReadOnlyList<Llamada>> BuscarLlamadasAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, int pagina, CancellationToken cancellationToken);

    // Agent/getTimes daily de un día
    Task<IReadOnlyList<TiempoAgente>> ObtenerTiemposAgentesAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken);

    // Agent/getStatus de un agente, null si no regresó datos
    Task<EstatusAgente?> ObtenerEstatusAgenteAsync(CuentaCcc cuenta, long userId, DateTime fechaFoto, CancellationToken cancellationToken);
}
