namespace Ccc.Descargas.Application.Llamadas;

public enum OrigenDescarga
{
    Automatica,   // el worker
    Manual,       // POST api/descargas o api/agentes
}

/// <summary>Qué se descargó. Es el TIPO de la bitácora y del marcador de avance.</summary>
public enum TipoDescarga
{
    Llamadas,          // CallHistory/Search
    EstatusAgentes,    // Agent/getStatus (foto en vivo)
    TiemposAgentes,    // Agent/getTimes daily
}

public static class TiposDescarga
{
    public static string Texto(TipoDescarga tipo) => tipo switch
    {
        TipoDescarga.Llamadas => "LLAMADAS",
        TipoDescarga.EstatusAgentes => "ESTATUS_AGENTES",
        TipoDescarga.TiemposAgentes => "TIEMPOS_AGENTES",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    public static TipoDescarga Desde(string texto) => texto switch
    {
        "LLAMADAS" => TipoDescarga.Llamadas,
        "ESTATUS_AGENTES" => TipoDescarga.EstatusAgentes,
        "TIEMPOS_AGENTES" => TipoDescarga.TiemposAgentes,
        _ => throw new ArgumentOutOfRangeException(nameof(texto), texto, "Tipo de descarga desconocido."),
    };
}

/// <summary>
/// Resultado de UN intento de descargar un tramo de una cuenta: lo que se guarda en la bitácora
/// (dbo.TBL_BITACORA_DESCARGAS_CCC) y en el reporte .txt de la cuenta.
/// Hasta va INCLUIDO (como datetime_to de CCC): de 10:00:00 a 10:00:59.
/// </summary>
public sealed record RegistroTramo(
    string ClientId,
    string NombreCuenta,
    TipoDescarga Tipo,
    DateTime Desde,
    DateTime Hasta,
    int? TamanoTramoSegundos,
    OrigenDescarga Origen,
    bool Exitosa,
    int Paginas,
    int Registros,
    int Insertadas,
    int Actualizadas,
    DateTime Inicio,
    DateTime Fin,
    string? Mensaje)
{
    /// <summary>Milisegundos esperando a CCC (incluye la fila del carril).</summary>
    public long? MsCcc { get; init; }

    /// <summary>Milisegundos guardando en la BD que NO se enciman con CCC (lo que de verdad atrasa).</summary>
    public long? MsBd { get; init; }
}

/// <summary>Marcador de avance de una cuenta y tipo: todo lo anterior a DescargadoHasta ya está.</summary>
public sealed record AvanceDescarga(
    DateTime DescargadoHasta, int? TamanoTramoSegundos, int ErroresSeguidos, string? UltimoError, DateTime? FechaUltimoError);

/// <summary>Resumen de la bitácora de una cuenta y tipo en un periodo (para los correos).</summary>
public sealed record ResumenTipo(TipoDescarga Tipo, int TramosOk, int TramosError, long Registros, DateTime? UltimoFin, string? UltimoError);

public sealed record ResumenCuenta(IReadOnlyList<ResumenTipo> Tipos, IReadOnlyDictionary<TipoDescarga, AvanceDescarga> Avance)
{
    public ResumenTipo? De(TipoDescarga tipo) => Tipos.FirstOrDefault(t => t.Tipo == tipo);
}
