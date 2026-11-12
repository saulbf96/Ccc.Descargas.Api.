using System.Security.Cryptography;
using System.Text;

namespace Ccc.Descargas.Infrastructure.Seguridad;

// secretos cifrados con DPAPI (dpapi: + base64)
public static class ProteccionDpapi
{
    public const string Prefijo = "dpapi:";

    // entropía extra
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("Ccc.Descargas");

    public static string DescifrarSiAplica(string valor, string queEs)
    {
        if (!valor.StartsWith(Prefijo, StringComparison.Ordinal))
        {
            return valor;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException($"No se puede descifrar {queEs}: DPAPI solo existe en Windows.");
        }

        try
        {
            var cifrado = Convert.FromBase64String(valor[Prefijo.Length..]);
            var descifrado = ProtectedData.Unprotect(cifrado, Entropia, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(descifrado);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // nunca el valor en el mensaje
            throw new InvalidOperationException(
                $"No se pudo descifrar {queEs}. ¿Se cifró en este mismo servidor y con la misma entropía?", ex);
        }
    }
}
