namespace Ccc.Descargas.Domain.Llamadas;

/// <summary>
/// Una llamada tal como la regresa CCC (CallHistory/Search). Se guarda TODO lo que trae:
/// los campos fijos como propiedades, los ref_str_* en DatosLead y cualquier campo
/// sin columna en la BD en CamposExtraJson.
/// </summary>
public sealed record Llamada
{
    public required int ClientId { get; init; }
    public required long CallId { get; init; }
    public required string CallDirection { get; init; }
    public long? CampaignId { get; init; }
    public string? CampaignType { get; init; }
    public string? CampaignName { get; init; }
    public string? Agent { get; init; }
    public long? UserId { get; init; }
    public required string CallerIdNumber { get; init; }
    public required string CalledNumber { get; init; }
    public string? Destination { get; init; }
    public required string AnswerState { get; init; }
    public string? AmdStatus { get; init; }
    public string? HangupReason { get; init; }
    public int? HangupCode { get; init; }
    public int? HangupCodeSip { get; init; }
    public required int DurationSecs { get; init; }
    public decimal? DurationMins { get; init; }
    public decimal? BillTime { get; init; }
    public decimal? BillRate { get; init; }
    public decimal? BillCost { get; init; }
    public required DateTime StartDatetime { get; init; }
    public DateTime? AnswerDatetime { get; init; }
    public DateTime? HangupDatetime { get; init; }
    public int? AccountNr { get; init; }
    public string? Tags { get; init; }
    public string? GrpAgentsIds { get; init; }
    public string? GrpAgentsNames { get; init; }
    public string? InstancesNames { get; init; }
    public string? GrpInstancesNames { get; init; }
    public long? ListId { get; init; }
    public long? LeadId { get; init; }
    public long? LeadEncryptedId { get; init; }
    public int? Attempts { get; init; }

    /// <summary>La tipificación de la llamada (primer elemento de "dispositions"), o null si no tiene.</summary>
    public Tipificacion? Tipificacion { get; init; }

    /// <summary>
    /// El arreglo "dispositions" completo, SOLO si trae más de una tipificación o una estructura
    /// desconocida (para no perder nada). En el caso normal va en null.
    /// </summary>
    public string? Dispositions { get; init; }

    public string? CamposExtraJson { get; init; }

    /// <summary>
    /// Campos ref_str_* con el nombre completo que manda CCC (ref_str_CURP, ref_str_CP...).
    /// Contiene datos personales: nunca escribirlos en logs.
    /// </summary>
    public IReadOnlyDictionary<string, string?> DatosLead { get; init; } = new Dictionary<string, string?>();
}
