using Ccc.Descargas.Application.Abstractions;

namespace Ccc.Descargas.Application.Cuentas;

public sealed class PingCuentasService(ICuentaCccRepository cuentas, ICccClient cccClient)
{
    public async Task<IReadOnlyList<ResultadoPing>> PingTodasAsync(CancellationToken cancellationToken)
    {
        // Las cuentas son independientes entre sí, así que se prueban en paralelo
        var tareas = cuentas.ObtenerTodas()
            .Select(cuenta => cccClient.PingAsync(cuenta, cancellationToken));

        return await Task.WhenAll(tareas);
    }

    public async Task<ResultadoPing?> PingAsync(string clientId, CancellationToken cancellationToken)
    {
        var cuenta = cuentas.ObtenerPorId(clientId);
        if (cuenta is null)
        {
            return null;
        }

        return await cccClient.PingAsync(cuenta, cancellationToken);
    }
}


