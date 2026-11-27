using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Api.Trabajos;

// arma el correo de estado de todas las cuentas (avance, atrasos por recuperar y errores)
internal sealed class EstadoDescargas(
    IServiceScopeFactory scopeFactory,
    IOptions<DescargaOptions> descarga,
    TimeProvider timeProvider)
{
    public async Task<ResumenDescargas.Correo> ArmarAsync(string titulo, DateTime desde, DateTime hasta, CancellationToken cancellationToken)
    {
        var ahora = timeProvider.GetLocalNow().DateTime;
        await using var scope = scopeFactory.CreateAsyncScope();
        var cuentas = scope.ServiceProvider.GetRequiredService<ICuentaCccRepository>().ObtenerTodas();
        var bitacora = scope.ServiceProvider.GetRequiredService<IBitacoraDescargas>();

        var resumenes = new List<(CuentaCcc, ResumenCuenta)>();
        var huecos = new Dictionary<string, IReadOnlyList<HuecoDescarga>>();
        foreach (var cuenta in cuentas)
        {
            resumenes.Add((cuenta, await bitacora.ObtenerResumenAsync(cuenta.ClientId, desde, hasta, cancellationToken)));
            huecos[cuenta.ClientId] = await bitacora.ObtenerHuecosAsync(cuenta.ClientId, cancellationToken);
        }

        var o = descarga.Value;
        return ResumenDescargas.Armar(titulo, resumenes, desde, hasta, ahora, ahora.AddMinutes(-o.MargenMinutos),
            TimeSpan.FromMinutes(o.MinutosAtrasoAlerta), Environment.MachineName, huecos);
    }
}
