using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Domain.Cuentas;

namespace Ccc.Descargas.Application.Abstractions;

/// <summary>
/// Contrato para hablar con CCC WebServices. Application no sabe
/// cómo se calcula el token ni cómo viaja el HTTP; eso lo resuelve Infrastructure.
/// </summary>
public interface ICccClient
{
    Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken);
}
