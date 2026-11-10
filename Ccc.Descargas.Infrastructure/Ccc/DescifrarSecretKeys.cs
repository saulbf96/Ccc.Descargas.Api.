using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Descifra UNA sola vez, al cargar la configuración, las SecretKey que vienen
/// cifradas con DPAPI (prefijo "dpapi:"). Las que no traen prefijo se usan tal cual,
/// por ejemplo las de User Secrets en desarrollo.
/// </summary>
internal sealed class DescifrarSecretKeys : IPostConfigureOptions<CccOptions>
{
    public void PostConfigure(string? name, CccOptions options)
    {
        foreach (var (clientId, cuenta) in options.Cuentas)
        {
            cuenta.SecretKey = ProteccionDpapi.DescifrarSiAplica(cuenta.SecretKey, $"la SecretKey de la cuenta {clientId}");
        }
    }
}
