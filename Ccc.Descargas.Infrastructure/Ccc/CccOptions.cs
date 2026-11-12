using System.ComponentModel.DataAnnotations;

namespace Ccc.Descargas.Infrastructure.Ccc;

// sección Ccc, las SecretKey van en User Secrets o variables de entorno
public sealed class CccOptions
{
    public const string Seccion = "Ccc";

    [Range(5, 600)]
    public int TimeoutSegundos { get; init; } = 120;

    // reintentos cuando CCC está ocupado (101)
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

   

    // set para reemplazarla descifrada
    [Required]
    public string SecretKey { get; set; } = string.Empty;


}