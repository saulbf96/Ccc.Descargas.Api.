namespace Ccc.Descargas.Application.Llamadas;

public enum EstadoTramo
{
    Completado,     // se descargó y guardó completo
    Error,          // falló; se puede volver a pedir
}

public sealed record ResultadoTramo(
    DateTime Desde, DateTime Hasta, EstadoTramo Estado, int Paginas, int Llamadas, int Insertadas, int Actualizadas, string? Mensaje);

/// <summary>Resultado de una descarga MANUAL de un rango (POST api/descargas).</summary>
public sealed record ResultadoDescarga(string ClientId, IReadOnlyList<ResultadoTramo> Tramos)
{
    public int TotalLlamadas => Tramos.Sum(t => t.Llamadas);

    public int TramosConError => Tramos.Count(t => t.Estado == EstadoTramo.Error);
}

/// <summary>
/// Lo que avanzó una cuenta en un turno de la descarga automática.
/// DescargadoHasta = todo lo anterior ya está; Error = el tramo que falló (se reintenta en el siguiente turno).
/// </summary>
public sealed record ResultadoTurno(
    string ClientId,
    int Tramos,
    int Llamadas,
    DateTime DescargadoHasta,
    DateTime Limite,
    RegistroTramo? Error)
{
    /// <summary>Lo que falta por bajar hasta el límite (cero = al día).</summary>
    public TimeSpan Atraso => DescargadoHasta >= Limite ? TimeSpan.Zero : Limite - DescargadoHasta;
}
