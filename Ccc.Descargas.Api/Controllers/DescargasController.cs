using System.ComponentModel.DataAnnotations;
using Ccc.Descargas.Application.Agentes;
using Ccc.Descargas.Application.Llamadas;
using Microsoft.AspNetCore.Mvc;

namespace Ccc.Descargas.Api.Controllers;

[ApiController]
[Route("api/descargas")]
public sealed class DescargasController(DescargarLlamadasService descargarLlamadas) : ControllerBase
{
    /// <summary>
    /// Descarga de CCC y guarda en la BD las llamadas de una cuenta en un rango (máximo 24 horas), por tramos.
    /// Repetir un rango no duplica: actualiza las llamadas que ya estaban y agrega las nuevas.
    /// No mueve el avance de la descarga automática.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ResultadoDescarga>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Descargar(SolicitudDescarga solicitud, CancellationToken cancellationToken)
    {
        // [Required] ya garantizó que vienen; aquí solo se toma el valor
        var desde = solicitud.Desde!.Value;
        var hasta = solicitud.Hasta!.Value;

        if (hasta <= desde)
        {
            return ValidationProblem(detail: "Hasta debe ser mayor que Desde.");
        }

        if (hasta - desde > DescargarLlamadasService.RangoMaximoManual)
        {
            // Protege a la API y a CCC de rangos enormes; los periodos largos los hace el proceso automático
            return ValidationProblem(detail: $"El rango máximo por petición es de {DescargarLlamadasService.RangoMaximoManual.TotalHours} horas.");
        }

        var resultado = await descargarLlamadas.DescargarRangoAsync(solicitud.ClientId, desde, hasta, cancellationToken);
        return resultado is null ? NotFound() : Ok(resultado);
    }
}

public sealed class SolicitudDescarga
{
    [Required]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    public DateTime? Desde { get; init; }

    /// <summary>No incluido: Desde 10:00 y Hasta 11:00 = de 10:00:00 a 10:59:59.</summary>
    [Required]
    public DateTime? Hasta { get; init; }
}

[ApiController]
[Route("api/agentes")]
public sealed class AgentesController(DescargarAgentesService agentes) : ControllerBase
{
    /// <summary>Toma ahora la foto de estatus de los agentes de una cuenta (y actualiza sus tiempos del día).</summary>
    [HttpPost("{clientId}/foto")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Foto(string clientId, CancellationToken cancellationToken)
    {
        var cuenta = agentes.ObtenerCuenta(clientId);
        if (cuenta is null)
        {
            return NotFound();
        }

        var (tiempos, estatus) = await agentes.TomarFotoAsync(cuenta, OrigenDescarga.Manual, cancellationToken);
        return Ok(new { tiempos, estatus });
    }

    /// <summary>Vuelve a bajar los tiempos de un día completo (no mueve el avance de la automática).</summary>
    [HttpPost("{clientId}/tiempos/{dia}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Tiempos(string clientId, DateOnly dia, CancellationToken cancellationToken)
    {
        var cuenta = agentes.ObtenerCuenta(clientId);
        if (cuenta is null)
        {
            return NotFound();
        }

        return Ok(await agentes.CerrarDiaAsync(cuenta, dia, OrigenDescarga.Manual, cancellationToken));
    }
}
