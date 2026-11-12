namespace Ccc.Descargas.Application.Llamadas;

public enum OrigenDescarga
{
    Automatica,   // el worker
    Manual,       // POST api/descargas o api/agentes
}

// tipo de descarga
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

// resultado de un tramo (Hasta incluido)
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
    // ms esperando a CCC
    public long? MsCcc { get; init; }

    // ms guardando en la BD
    public long? MsBd { get; init; }
}

// marcador de avance
public sealed record AvanceDescarga(
    DateTime DescargadoHasta, int? TamanoTramoSegundos, int ErroresSeguidos, string? UltimoError, DateTime? FechaUltimoError);

// resumen de bitácora para los correos
public sealed record ResumenTipo(TipoDescarga Tipo, int TramosOk, int TramosError, long Registros, DateTime? UltimoFin, string? UltimoError);

public sealed record ResumenCuenta(IReadOnlyList<ResumenTipo> Tipos, IReadOnlyDictionary<TipoDescarga, AvanceDescarga> Avance)
{
    public ResumenTipo? De(TipoDescarga tipo) => Tipos.FirstOrDefault(t => t.Tipo == tipo);
}
