using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

public interface ILlamadaRepository
{
    // guarda un lote: inserta nuevas y actualiza existentes
    Task<(int Insertadas, int Actualizadas)> GuardarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken cancellationToken);
}

public interface IAgenteRepository
{
    // fotos de estatus
    Task<int> GuardarEstatusAsync(string clientId, IReadOnlyList<EstatusAgente> fotos, CancellationToken cancellationToken);

    // tiempos por agente y día
    Task<(int Insertadas, int Actualizadas)> GuardarTiemposAsync(
        string clientId, IReadOnlyList<TiempoAgente> tiempos, CancellationToken cancellationToken);
}
