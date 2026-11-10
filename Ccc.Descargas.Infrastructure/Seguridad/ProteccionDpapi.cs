using System.Security.Cryptography;
using System.Text;

namespace Ccc.Descargas.Infrastructure.Seguridad;

/// <summary>
/// Secretos cifrados con DPAPI en el servidor (prefijo "dpapi:" + base64), con la cuenta de la MÁQUINA:
/// solo se pueden descifrar en el mismo servidor donde se cifraron (herramientas\Instalar-Servicio.ps1).
/// Los valores sin prefijo se usan tal cual, por ejemplo los de User Secrets en desarrollo.
/// </summary>
public static class ProteccionDpapi
{
    public const string Prefijo = "dpapi:";

    // Valor extra que se mezcla en el cifrado: sin él no se puede descifrar,
    // aunque otro programa del mismo servidor intente usar DPAPI
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("Ccc.Descargas");

    /// <param name="valor">El valor de la configuración, cifrado o no.</param>
    /// <param name="queEs">Para el mensaje de error, ej. "la SecretKey de la cuenta 2812". Nunca el valor.</param>
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
            // Nunca incluir el valor en el mensaje: solo qué falló
            throw new InvalidOperationException(
                $"No se pudo descifrar {queEs}. ¿Se cifró en este mismo servidor y con la misma entropía?", ex);
        }
    }
}
