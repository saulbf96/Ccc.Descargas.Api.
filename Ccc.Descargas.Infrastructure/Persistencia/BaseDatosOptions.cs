using System.ComponentModel.DataAnnotations;
using Ccc.Descargas.Infrastructure.Seguridad;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Persistencia;

// sección BaseDatos, la cadena va en User Secrets o cifrada con DPAPI
public sealed class BaseDatosOptions
{
    public const string Seccion = "BaseDatos";

    // set para reemplazarla descifrada
    [Required]
    public string CadenaConexion { get; set; } = string.Empty;

    [Range(5, 600)]
    public int TimeoutComandoSegundos { get; init; } = 60;
}

// descifra la cadena de conexión
internal sealed class DescifrarCadenaConexion : IPostConfigureOptions<BaseDatosOptions>
{
    public void PostConfigure(string? name, BaseDatosOptions options) =>
        options.CadenaConexion = ProteccionDpapi.DescifrarSiAplica(options.CadenaConexion, "la cadena de conexión (BaseDatos:CadenaConexion)");
}
