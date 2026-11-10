using System.ComponentModel.DataAnnotations;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Sección "Ccc" de la configuración. Los datos públicos van en appsettings.json;
/// las SecretKey van en User Secrets (desarrollo) o variables de entorno (servidor).
/// </summary>
public sealed class CccOptions
{
    public const string Seccion = "Ccc";

    [Range(5, 600)]
    public int TimeoutSegundos { get; init; } = 120;

    /// <summary>
    /// CCC atiende una sola petición a la vez: si está ocupado responde 400 con code 101
    /// ("There is another Transaction in Progress"). Se espera y se reintenta.
    /// </summary>
    [Range(0, 60)]
    public int ReintentosOcupado { get; init; } = 10;

    [Range(1, 600)]
    public int EsperaOcupadoSegundos { get; init; } = 30;

    [MinLength(1)]
    public Dictionary<string, CuentaCccOptions> Cuentas { get; init; } = [];
}

public sealed class CuentaCccOptions
{
    [Required]
    public string Nombre { get; init; } = string.Empty;

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string Username { get; init; } = string.Empty;

   

    // set (no init) para que DescifrarSecretKeys pueda reemplazarla ya descifrada
    [Required]
    public string SecretKey { get; set; } = string.Empty;


}