namespace Ccc.Descargas.Domain.Agentes;

/// <summary>
/// Tiempos de UN agente en UN día (Agent/getTimes type=daily). Las pausas cambian por cuenta (baño, comida,
/// capacitación...): van en PausasJson como {tipo: minutos}. Lo demás que mande CCC sin propiedad aquí
/// (los hh:mm:ss, tiempo excedido...) va en CamposExtraJson para no perder nada.
/// </summary>
public sealed record TiempoAgente
{
    public required long AgentId { get; init; }
    public required DateOnly Date { get; init; }
    public string? AgentUsername { get; init; }
    public string? AgentName { get; init; }
    public DateTime? LoginDatetime { get; init; }
    public DateTime? LogoutDatetime { get; init; }
    public int? CallsHandled { get; init; }
    public int? CallsHungup { get; init; }
    public int? CallsTransfer { get; init; }
    public decimal? Aht { get; init; }
    public decimal? StatusAvailPercent { get; init; }
    public decimal? StatusOncallPercent { get; init; }
    public decimal? StatusAftercallPercent { get; init; }
    public decimal? StatusPausePercent { get; init; }
    public decimal? StatusOthersPercent { get; init; }
    public decimal? TotalTimeMinutes { get; init; }
    public decimal? StatusOnlineMinutes { get; init; }
    public decimal? StatusAvailMinutes { get; init; }
    public decimal? StatusOncallMinutes { get; init; }
    public decimal? StatusAftercallMinutes { get; init; }
    public decimal? StatusCallManualMinutes { get; init; }
    public decimal? StatusCallConferenceMinutes { get; init; }
    public decimal? StatusPauseMinutes { get; init; }
    public decimal? StatusMonitoringMinutes { get; init; }
    public decimal? StatusWaitCampaignMinutes { get; init; }
    public string? PausasJson { get; init; }
    public string? CamposExtraJson { get; init; }
}
