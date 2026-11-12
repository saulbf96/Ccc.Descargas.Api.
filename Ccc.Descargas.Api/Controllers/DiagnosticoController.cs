using Ccc.Descargas.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ccc.Descargas.Api.Controllers;

[ApiController]
[Route("api/diagnostico")]
public sealed class DiagnosticoController(INotificador notificador) : ControllerBase
{
    // manda un correo de prueba
    [HttpPost("correo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ProbarCorreo(CancellationToken cancellationToken)
    {
        var error = await notificador.ProbarAsync(cancellationToken);
        return error is null
            ? Ok(new { enviado = true, mensaje = "Correo de prueba enviado: revisa la bandeja (y spam)." })
            : StatusCode(StatusCodes.Status502BadGateway, new { enviado = false, mensaje = error });
    }
}
