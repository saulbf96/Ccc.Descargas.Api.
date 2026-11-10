using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Cuentas;
using Ccc.Descargas.Domain.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

/// <summary>
/// Contrato para hablar con CCC WebServices. Application no sabe cómo se calcula el token ni cómo viaja el
/// HTTP; eso lo resuelve Infrastructure. Cada plataforma (CCC1, CCC2) atiende UNA petición a la vez: la
/// implementación las forma en fila por plataforma, sin importar de qué cuenta sean.
/// Todos (menos Ping) lanzan CccApiException si CCC no responde 200.
/// </summary>
public interface ICccClient
{
    Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken);

    /// <summary>Una página de CallHistory/Search (desde y hasta incluidos).</summary>
    Task<IReadOnlyList<Llamada>> BuscarLlamadasAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, int pagina, CancellationToken cancellationToken);

    /// <summary>Agent/getTimes type=daily de un día: una fila por agente que trabajó ese día.</summary>
    Task<IReadOnlyList<TiempoAgente>> ObtenerTiemposAgentesAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken);

    /// <summary>Agent/getStatus de UN agente. Null si CCC no regresó datos para ese agente.</summary>
    Task<EstatusAgente?> ObtenerEstatusAgenteAsync(CuentaCcc cuenta, long userId, DateTime fechaFoto, CancellationToken cancellationToken);
}
