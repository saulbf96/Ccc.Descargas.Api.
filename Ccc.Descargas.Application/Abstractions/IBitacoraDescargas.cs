using Ccc.Descargas.Application.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

// bitácora por tramo y avance de cada cuenta
public interface IBitacoraDescargas
{
    // moverAvance: si se completó, el avance pasa a Hasta + 1 s
    Task RegistrarAsync(RegistroTramo registro, bool moverAvance, CancellationToken cancellationToken);

    // dónde se quedó la cuenta, null si nunca se ha descargado
    Task<AvanceDescarga?> ObtenerAvanceAsync(string clientId, TipoDescarga tipo, CancellationToken cancellationToken);

    Task<ResumenCuenta> ObtenerResumenAsync(string clientId, DateTime desde, DateTime hasta, CancellationToken cancellationToken);
}

// reporte .txt por cuenta y día
public interface IReporteDescargas
{
    Task EscribirTramoAsync(RegistroTramo registro, CancellationToken cancellationToken);

    // aviso general, sin cuenta va en todos los reportes
    Task EscribirAvisoAsync(string? clientId, string mensaje, CancellationToken cancellationToken);
}

// avisos por correo
public interface INotificador
{
    Task EnviarAsync(string asunto, string cuerpo, CancellationToken cancellationToken);

    // correo de prueba, regresa null si salió bien
    Task<string?> ProbarAsync(CancellationToken cancellationToken);
}
