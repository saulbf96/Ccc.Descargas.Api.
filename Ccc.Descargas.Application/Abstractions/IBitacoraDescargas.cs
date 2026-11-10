using Ccc.Descargas.Application.Llamadas;

namespace Ccc.Descargas.Application.Abstractions;

/// <summary>
/// Bitácora de descargas (un renglón por tramo) y marcador de avance de cada cuenta, en la BD.
/// Todo pasa por el SP único de la cuenta (dbo.USP_CCC_&lt;cuenta&gt;).
/// </summary>
public interface IBitacoraDescargas
{
    /// <param name="moverAvance">true = si el tramo se completó el avance pasa a Hasta + 1 s; si falló, cuenta el error.</param>
    Task RegistrarAsync(RegistroTramo registro, bool moverAvance, CancellationToken cancellationToken);

    /// <summary>Dónde se quedó la cuenta; null si nunca se ha descargado.</summary>
    Task<AvanceDescarga?> ObtenerAvanceAsync(string clientId, TipoDescarga tipo, CancellationToken cancellationToken);

    Task<ResumenCuenta> ObtenerResumenAsync(string clientId, DateTime desde, DateTime hasta, CancellationToken cancellationToken);
}

/// <summary>
/// Reporte en texto, un archivo por cuenta y por día, con cada descarga e incidencia.
/// </summary>
public interface IReporteDescargas
{
    Task EscribirTramoAsync(RegistroTramo registro, CancellationToken cancellationToken);

    /// <summary>Un aviso (ej. la app estuvo detenida). Sin cuenta = se escribe en el reporte de todas.</summary>
    Task EscribirAvisoAsync(string? clientId, string mensaje, CancellationToken cancellationToken);
}

/// <summary>
/// Avisos fuera de la aplicación (correo). Si no está configurado, no hace nada.
/// </summary>
public interface INotificador
{
    Task EnviarAsync(string asunto, string cuerpo, CancellationToken cancellationToken);

    /// <summary>Manda un correo de prueba. Regresa null si salió, o el motivo de la falla.</summary>
    Task<string?> ProbarAsync(CancellationToken cancellationToken);
}
