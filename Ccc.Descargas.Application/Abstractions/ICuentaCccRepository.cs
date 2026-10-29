using Ccc.Descargas.Domain.Cuentas;

namespace Ccc.Descargas.Application.Abstractions;

public interface ICuentaCccRepository
{
    IReadOnlyList<CuentaCcc> ObtenerTodas();

    CuentaCcc? ObtenerPorId(string clientId);
}


