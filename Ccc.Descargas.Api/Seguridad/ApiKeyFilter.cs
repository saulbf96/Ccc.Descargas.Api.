using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Seguridad;

/// <summary>
/// Sección "Seguridad". La ApiKey NUNCA va en appsettings: en desarrollo va en User Secrets
/// (Seguridad:ApiKey) y en el servidor como variable del servicio cifrada con DPAPI (Seguridad__ApiKey).
/// </summary>
public sealed class SeguridadOptions
{
    public const string Seccion = "Seguridad";

    // set (no init) para que DescifrarApiKey la reemplace ya descifrada
    [Required, MinLength(32)]
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Descifra la ApiKey una sola vez, al cargar la configuración.</summary>
internal sealed class DescifrarApiKey : IPostConfigureOptions<SeguridadOptions>
{
    public void PostConfigure(string? name, SeguridadOptions options) =>
        options.ApiKey = ProteccionDpapi.DescifrarSiAplica(options.ApiKey, "la ApiKey (Seguridad:ApiKey)");
}

/// <summary>
/// Todos los endpoints exigen el encabezado X-Api-Key con la clave configurada; si no, 401.
/// </summary>
internal sealed class ApiKeyFilter(IOptions<SeguridadOptions> options) : IAuthorizationFilter
{
    public const string Encabezado = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var recibida = context.HttpContext.Request.Headers[Encabezado].ToString();
        if (!SonIguales(recibida, options.Value.ApiKey))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    // Compara hashes en tiempo constante: no deja adivinar la clave por lo que tarda la respuesta
    private static bool SonIguales(string recibida, string esperada) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(recibida)),
            SHA256.HashData(Encoding.UTF8.GetBytes(esperada)));
}
