namespace Ccc.Descargas.Application.Llamadas;

public enum EstadoTramo
{
    Completado,     // se descargó y guardó completo
    Error,          // falló; se puede volver a pedir
}

public sealed record ResultadoTramo(
    DateTime Desde, DateTime Hasta, EstadoTramo Estado, int Paginas, int Llamadas, int Insertadas, int Actualizadas, string? Mensaje);

// resultado de descarga manual
public sealed record ResultadoDescarga(string ClientId, IReadOnlyList<ResultadoTramo> Tramos)
{
    public int TotalLlamadas => Tramos.Sum(t => t.Llamadas);

    public int TramosConError => Tramos.Count(t => t.Estado == EstadoTramo.Error);
}

// lo que avanzó una cuenta en un turno
public sealed record ResultadoTurno(
    string ClientId,
    int Tramos,
    int Llamadas,
    DateTime DescargadoHasta,
    DateTime Limite,
    RegistroTramo? Error)
{
    // lo que falta, cero = al día
    public TimeSpan Atraso => DescargadoHasta >= Limite ? TimeSpan.Zero : Limite - DescargadoHasta;
}
