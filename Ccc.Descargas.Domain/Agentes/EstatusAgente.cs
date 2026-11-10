namespace Ccc.Descargas.Domain.Agentes;

/// <summary>
/// Foto en vivo de UN agente (Agent/getStatus): su estado en ese momento, desde cuándo, la llamada en curso
/// y el lead. Se guarda TODO: los ref_str_* del lead en DatosLeadJson y cualquier campo desconocido en
/// CamposExtraJson.
/// </summary>
public sealed record EstatusAgente
{
    /// <summary>Hora (de este servidor) en que se tomó la foto; la misma para todos los agentes de una vuelta.</summary>
    public required DateTime FechaFoto { get; init; }
    public required long UserId { get; init; }

    /// <summary>getStatus no los trae: salen de Agent/getTimes.</summary>
    public string? AgentUsername { get; init; }
    public string? AgentName { get; init; }

    public long? LoginId { get; init; }
    public string? AgentStatus { get; init; }
    public DateTime? AgentStatusDatetime { get; init; }
    public int? AgentStatusTime { get; init; }
    public long? CallId { get; init; }
    public string? CallState { get; init; }
    public string? CallCallerIdNumber { get; init; }
    public string? CallDestination { get; init; }
    public long? CampaignId { get; init; }
    public long? LeadId { get; init; }
    public long? ListId { get; init; }
    public string? UseDbRtx { get; init; }
    public string? DatosLeadJson { get; init; }
    public string? CamposExtraJson { get; init; }
}
