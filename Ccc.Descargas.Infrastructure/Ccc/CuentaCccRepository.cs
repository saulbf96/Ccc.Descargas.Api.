using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Por ahora las cuentas salen de la configuración. Si después viven en BD,
/// solo se cambia esta clase; Application no se entera.
/// </summary>
/// 


internal sealed class CuentaCccRepository(IOptions<CccOptions> options) : ICuentaCccRepository
{
    public IReadOnlyList<CuentaCcc> ObtenerTodas() =>
        options.Value.Cuentas
            .Select(par => Crear(par.Key, par.Value))
            .ToList();

    public CuentaCcc? ObtenerPorId(string clientId) =>
        options.Value.Cuentas.TryGetValue(clientId, out var cuenta)
            ? Crear(clientId, cuenta)
            : null;

    private static CuentaCcc Crear(string clientId, CuentaCccOptions cuenta) =>
        new(clientId, cuenta.Nombre, new Uri(cuenta.BaseUrl).Host);
}

