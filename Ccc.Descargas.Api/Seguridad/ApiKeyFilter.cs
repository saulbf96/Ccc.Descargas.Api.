using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Seguridad;

// ApiKey: en desarrollo en User Secrets, en el servidor cifrada con DPAPI
public sealed class SeguridadOptions
{
    public const string Seccion = "Seguridad";

    // set para poder reemplazarla ya descifrada
    [Required, MinLength(32)]
    public string ApiKey { get; set; } = string.Empty;
}

// descifra la ApiKey al cargar la configuración
internal sealed class DescifrarApiKey : IPostConfigureOptions<SeguridadOptions>
{
    public void PostConfigure(string? name, SeguridadOptions options) =>
        options.ApiKey = ProteccionDpapi.DescifrarSiAplica(options.ApiKey, "la ApiKey (Seguridad:ApiKey)");
}

// todos los endpoints piden X-Api-Key
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

    // comparación en tiempo constante
    private static bool SonIguales(string recibida, string esperada) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(recibida)),
            SHA256.HashData(Encoding.UTF8.GetBytes(esperada)));
}
