using System.ComponentModel.DataAnnotations;
using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Persistencia;

/// <summary>
/// Sección "BaseDatos". La cadena de conexión con usuario y contraseña NUNCA va en
/// appsettings.json: en desarrollo va en User Secrets o en appsettings.Development.json
/// (si es autenticación de Windows, sin contraseña); en el servidor, variable de entorno
/// del servicio cifrada con DPAPI ("dpapi:...").
/// </summary>
public sealed class BaseDatosOptions
{
    public const string Seccion = "BaseDatos";

    // set (no init) para que DescifrarCadenaConexion la reemplace ya descifrada
    [Required]
    public string CadenaConexion { get; set; } = string.Empty;

    [Range(5, 600)]
    public int TimeoutComandoSegundos { get; init; } = 60;
}

/// <summary>Descifra la cadena de conexión una sola vez, al cargar la configuración.</summary>
internal sealed class DescifrarCadenaConexion : IPostConfigureOptions<BaseDatosOptions>
{
    public void PostConfigure(string? name, BaseDatosOptions options) =>
        options.CadenaConexion = ProteccionDpapi.DescifrarSiAplica(options.CadenaConexion, "la cadena de conexión (BaseDatos:CadenaConexion)");
}
