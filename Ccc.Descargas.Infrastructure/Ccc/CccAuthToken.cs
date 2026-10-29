using System.Security.Cryptography;
using System.Text;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Fórmula de la sección "Authentication Protocol" de la documentación de CCC.
/// </summary>
internal static class CccAuthToken
{
    public static string Calcular(string clientId, string username, string secretKey, string data, string requestDateTime)
    {
        var token1 = Sha256Hex(data);
        var secretHash = Sha256Hex(secretKey);
        var token2 = Sha256Hex($"{clientId}:{username}:{secretHash}");
        return Sha256Hex($"{token1}:{token2}:{requestDateTime}");
    }

    private static string Sha256Hex(string texto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));


}