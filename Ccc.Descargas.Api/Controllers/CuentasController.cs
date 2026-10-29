using Ccc.Descargas.Application.Cuentas;
using Microsoft.AspNetCore.Mvc;

namespace Ccc.Descargas.Api.Controllers;

[ApiController]
[Route("api/cuentas")]
public sealed class CuentasController(PingCuentasService pingCuentas) : ControllerBase
{
    [HttpGet("ping")]
    [ProducesResponseType<IReadOnlyList<ResultadoPing>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PingTodas(CancellationToken cancellationToken)
    {
        var resultados = await pingCuentas.PingTodasAsync(cancellationToken);
        return Ok(resultados);
    }

    [HttpGet("{clientId}/ping")]
    [ProducesResponseType<ResultadoPing>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ping(string clientId, CancellationToken cancellationToken)
    {
        var resultado = await pingCuentas.PingAsync(clientId, cancellationToken);
        return resultado is null ? NotFound() : Ok(resultado);
    }
}