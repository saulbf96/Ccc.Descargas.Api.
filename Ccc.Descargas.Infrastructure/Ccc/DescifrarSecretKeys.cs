using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

// descifra las SecretKey con prefijo dpapi:
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
