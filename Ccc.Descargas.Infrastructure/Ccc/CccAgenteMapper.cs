using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ccc.Descargas.Domain.Agentes;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Convierte las respuestas de Agent/getStatus y Agent/getTimes sin perder nada: campos conocidos a
/// propiedades, ref_str_* del lead a DatosLeadJson, pausas por tipo a PausasJson y lo demás a CamposExtraJson.
/// Tolerante: un valor que no se puede convertir queda en NULL y su texto original se guarda en CamposExtraJson.
/// </summary>
internal static partial class CccAgenteMapper
{
    private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

    private static readonly HashSet<string> CamposEstatus =
    [
        "USE_DB_RTX", "login_id", "user_id", "call_id", "agent_status", "agent_status_datetime", "agent_status_time",
        "call_callerid_number", "call_state", "campaign_id", "lead_id", "list_id", "call_destination",
    ];

    private static readonly HashSet<string> CamposTiempo =
    [
        "agent_id", "date", "agent_username", "agent_name", "login_datetime", "logout_datetime", "calls_handled",
        "calls_hungup", "calls_transfer", "aht", "status_avail_percent", "status_oncall_percent",
        "status_aftercall_percent", "status_pause_percent", "status_others_percent", "total_time_minutes",
        "status_online_minutes", "status_avail_minutes", "status_oncall_minutes", "status_aftercall_minutes",
        "status_call_manual_minutes", "status_call_conference_minutes", "status_pause_minutes",
        "status_monitoring_minutes", "status_wait_campaign_minutes",
    ];

    // status_pause_baÑo_minutes -> "baÑo". status_pause_minutes (el total) NO entra: tiene su columna.
    [GeneratedRegex(@"^status_pause_(.+)_minutes$")]
    private static partial Regex Pausa();

    public static EstatusAgente MapearEstatus(JsonElement json, long userId, DateTime fechaFoto)
    {
        var lead = new JsonObject();
        var extras = new JsonObject();
        foreach (var p in json.EnumerateObject())
        {
            if (p.Name.StartsWith("ref_str_", StringComparison.Ordinal))
            {
                lead[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            }
            else if (!CamposEstatus.Contains(p.Name))
            {
                extras[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            }
        }

        var lector = new Lector(json, extras);
        return new EstatusAgente
        {
            FechaFoto = fechaFoto,
            UserId = lector.Entero64("user_id") ?? userId,
            LoginId = lector.Entero64("login_id"),
            AgentStatus = lector.Texto("agent_status"),
            AgentStatusDatetime = lector.Fecha("agent_status_datetime"),
            AgentStatusTime = (int?)lector.Entero64("agent_status_time"),
            CallId = lector.Entero64("call_id"),
            CallState = lector.Texto("call_state"),
            CallCallerIdNumber = lector.Texto("call_callerid_number"),
            CallDestination = lector.Texto("call_destination"),
            CampaignId = lector.Entero64("campaign_id"),
            LeadId = lector.Entero64("lead_id"),
            ListId = lector.Entero64("list_id"),
            UseDbRtx = lector.Texto("USE_DB_RTX"),
            DatosLeadJson = lead.Count > 0 ? lead.ToJsonString() : null,
            CamposExtraJson = extras.Count > 0 ? extras.ToJsonString() : null,
        };
    }

    public static TiempoAgente MapearTiempo(JsonElement json)
    {
        var pausas = new JsonObject();
        var extras = new JsonObject();
        foreach (var p in json.EnumerateObject())
        {
            if (CamposTiempo.Contains(p.Name))
            {
                continue;
            }

            var pausa = Pausa().Match(p.Name);
            if (pausa.Success && TryDecimal(p.Value, out var minutos))
            {
                pausas[pausa.Groups[1].Value] = minutos;
            }
            else
            {
                extras[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            }
        }

        var l = new Lector(json, extras);
        var fecha = l.Texto("date");
        return new TiempoAgente
        {
            AgentId = l.Entero64("agent_id") ?? throw new JsonException("Agent/getTimes regresó un agente sin agent_id."),
            Date = DateOnly.ParseExact(fecha ?? throw new JsonException("Agent/getTimes regresó un agente sin date."),
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            AgentUsername = l.Texto("agent_username"),
            AgentName = l.Texto("agent_name"),
            LoginDatetime = l.Fecha("login_datetime"),
            LogoutDatetime = l.Fecha("logout_datetime"),
            CallsHandled = (int?)l.Entero64("calls_handled"),
            CallsHungup = (int?)l.Entero64("calls_hungup"),
            CallsTransfer = (int?)l.Entero64("calls_transfer"),
            Aht = l.Decimal("aht"),
            StatusAvailPercent = l.Decimal("status_avail_percent"),
            StatusOncallPercent = l.Decimal("status_oncall_percent"),
            StatusAftercallPercent = l.Decimal("status_aftercall_percent"),
            StatusPausePercent = l.Decimal("status_pause_percent"),
            StatusOthersPercent = l.Decimal("status_others_percent"),
            TotalTimeMinutes = l.Decimal("total_time_minutes"),
            StatusOnlineMinutes = l.Decimal("status_online_minutes"),
            StatusAvailMinutes = l.Decimal("status_avail_minutes"),
            StatusOncallMinutes = l.Decimal("status_oncall_minutes"),
            StatusAftercallMinutes = l.Decimal("status_aftercall_minutes"),
            StatusCallManualMinutes = l.Decimal("status_call_manual_minutes"),
            StatusCallConferenceMinutes = l.Decimal("status_call_conference_minutes"),
            StatusPauseMinutes = l.Decimal("status_pause_minutes"),
            StatusMonitoringMinutes = l.Decimal("status_monitoring_minutes"),
            StatusWaitCampaignMinutes = l.Decimal("status_wait_campaign_minutes"),
            PausasJson = pausas.Count > 0 ? pausas.ToJsonString() : null,
            CamposExtraJson = extras.Count > 0 ? extras.ToJsonString() : null,
        };
    }

    private static bool TryDecimal(JsonElement valor, out decimal resultado)
    {
        var texto = valor.ValueKind == JsonValueKind.String ? valor.GetString() : valor.GetRawText();
        return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out resultado);
    }

    /// <summary>Lee campos; si uno no se puede convertir, guarda su texto original en extras.</summary>
    private readonly struct Lector(JsonElement json, JsonObject extras)
    {
        public string? Texto(string campo)
        {
            if (!json.TryGetProperty(campo, out var valor))
            {
                return null;
            }

            var texto = valor.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => valor.GetString(),
                _ => valor.GetRawText(),
            };
            return string.IsNullOrWhiteSpace(texto) ? null : texto;
        }

        public long? Entero64(string campo) =>
            Convertir<long>(campo, t => long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null);

        public decimal? Decimal(string campo) =>
            Convertir<decimal>(campo, t => decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null);

        public DateTime? Fecha(string campo) =>
            Convertir<DateTime>(campo, t => DateTime.TryParseExact(t, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
                && v.Year > 1 ? v : null);

        private T? Convertir<T>(string campo, Func<string, T?> convertir) where T : struct
        {
            var texto = Texto(campo);
            if (texto is null)
            {
                return null;
            }

            var valor = convertir(texto);
            if (valor is null)
            {
                extras[campo] = texto;   // no se pierde: se guarda tal cual
            }

            return valor;
        }
    }
}
