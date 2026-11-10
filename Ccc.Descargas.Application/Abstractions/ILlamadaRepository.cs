using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

public interface ILlamadaRepository
{
    /// <summary>
    /// Guarda un lote en una sola transacción: inserta las nuevas y actualiza las existentes.
    /// </summary>
    Task<(int Insertadas, int Actualizadas)> GuardarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken cancellationToken);
}

public interface IAgenteRepository
{
    /// <summary>Guarda las fotos de estatus (solo inserta; repetir la misma foto no duplica).</summary>
    Task<int> GuardarEstatusAsync(string clientId, IReadOnlyList<EstatusAgente> fotos, CancellationToken cancellationToken);

    /// <summary>Guarda los tiempos por agente y día (inserta o actualiza).</summary>
    Task<(int Insertadas, int Actualizadas)> GuardarTiemposAsync(
        string clientId, IReadOnlyList<TiempoAgente> tiempos, CancellationToken cancellationToken);
}
