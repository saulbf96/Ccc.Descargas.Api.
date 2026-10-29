using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Descifra UNA sola vez, al cargar la configuración, las SecretKey que vienen
/// cifradas con DPAPI (prefijo "dpapi:"). Las que no traen prefijo se usan tal cual,
/// por ejemplo las de User Secrets en desarrollo.
/// </summary>
internal sealed class DescifrarSecretKeys : IPostConfigureOptions<CccOptions>
{
    public const string Prefijo = "dpapi:";

    // Valor extra que se mezcla en el cifrado: sin él no se puede descifrar,
    // aunque otro programa del mismo servidor intente usar DPAPI
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("Ccc.Descargas");

    public void PostConfigure(string? name, CccOptions options)
    {
        foreach (var (clientId, cuenta) in options.Cuentas)
        {
            if (!cuenta.SecretKey.StartsWith(Prefijo, StringComparison.Ordinal))
            {
                continue;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Las SecretKey cifradas con DPAPI solo se pueden descifrar en Windows.");
            }

            try
            {
                var cifrado = Convert.FromBase64String(cuenta.SecretKey[Prefijo.Length..]);
                var descifrado = ProtectedData.Unprotect(cifrado, Entropia, DataProtectionScope.LocalMachine);
                cuenta.SecretKey = Encoding.UTF8.GetString(descifrado);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Nunca incluir el valor en el mensaje: solo qué cuenta falló
                throw new InvalidOperationException(
                    $"No se pudo descifrar la SecretKey de la cuenta {clientId}. " +
                    "¿Se cifró en este mismo servidor y con la misma entropía?", ex);
            }
        }
    }
}