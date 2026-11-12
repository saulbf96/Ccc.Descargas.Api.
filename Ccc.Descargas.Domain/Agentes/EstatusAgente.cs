namespace Ccc.Descargas.Domain.Agentes;

// foto de estatus de un agente (Agent/getStatus)
public sealed record EstatusAgente
{
    // hora de la foto
    public required DateTime FechaFoto { get; init; }
    public required long UserId { get; init; }

    // vienen de getTimes
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
