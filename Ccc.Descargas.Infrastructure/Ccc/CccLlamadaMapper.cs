using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ccc.Descargas.Domain.Llamadas;
using Ccc.Descargas.Infrastructure.Persistencia;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Convierte cada elemento de "results" de CallHistory/Search en una Llamada sin perder nada:
/// campos conocidos -> propiedades, ref_str_* con columna en la tabla de la cuenta -> DatosLead,
/// dispositions -> Tipificacion, y cualquier otro campo (ej. un ref_str_ nuevo) -> CamposExtraJson.
/// </summary>
internal static partial class CccLlamadaMapper
{
    private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

    private static readonly HashSet<string> CamposConocidos =
    [
        "call_id", "call_direction", "campaign_id", "campaign_type", "campaign_name", "agent", "user_id",
        "callerid_number", "called_number", "destination", "answer_state", "amd_status", "hangup_reason",
        "hangup_code", "hangup_code_sip", "duration_secs", "duration_mins", "bill_time", "bill_rate", "bill_cost",
        "start_datetime", "answer_datetime", "hangup_datetime", "account_nr", "tags", "grp_agents_ids",
        "grp_agents_names", "instances_names", "grp_instances_names", "list_id", "lead_id",
        "lead_encrypted_id", "attempts", "dispositions",
    ];

    public static Llamada Mapear(JsonElement json, int clientId)
    {
        var datosLead = new Dictionary<string, string?>();
        var extras = new JsonObject();

        foreach (var propiedad in json.EnumerateObject())
        {
            var columna = NombreColumna(propiedad.Name);
            if (TablasCuenta.TieneColumna(clientId, columna) && !datosLead.ContainsKey(columna))
            {
                datosLead[columna] = Texto(propiedad.Value);
            }
            else if (!CamposConocidos.Contains(propiedad.Name))
            {
                extras[propiedad.Name] = JsonNode.Parse(propiedad.Value.GetRawText());
            }
        }

        var (tipificacion, dispositions) = LeerTipificacion(json);

        return new Llamada
        {
            ClientId = clientId,
            CallId = Entero64(json, "call_id") ?? throw new JsonException("La llamada no trae call_id."),
            CallDirection = Texto(json, "call_direction") ?? string.Empty,
            CampaignId = Entero64(json, "campaign_id"),
            CampaignType = Texto(json, "campaign_type"),
            CampaignName = Texto(json, "campaign_name"),
            Agent = Texto(json, "agent"),
            UserId = Entero64(json, "user_id"),
            CallerIdNumber = Texto(json, "callerid_number") ?? string.Empty,
            CalledNumber = Texto(json, "called_number") ?? string.Empty,
            Destination = Texto(json, "destination"),
            AnswerState = Texto(json, "answer_state") ?? string.Empty,
            AmdStatus = Texto(json, "amd_status"),
            HangupReason = Texto(json, "hangup_reason"),
            HangupCode = (int?)Entero64(json, "hangup_code"),
            HangupCodeSip = (int?)Entero64(json, "hangup_code_sip"),
            DurationSecs = (int?)Entero64(json, "duration_secs") ?? 0,
            DurationMins = Decimal(json, "duration_mins"),
            BillTime = Decimal(json, "bill_time"),
            BillRate = Decimal(json, "bill_rate"),
            BillCost = Decimal(json, "bill_cost"),
            StartDatetime = Fecha(json, "start_datetime") ?? throw new JsonException("La llamada no trae start_datetime."),
            AnswerDatetime = Fecha(json, "answer_datetime"),
            HangupDatetime = Fecha(json, "hangup_datetime"),
            AccountNr = (int?)Entero64(json, "account_nr"),
            Tags = Texto(json, "tags"),
            GrpAgentsIds = Texto(json, "grp_agents_ids"),
            GrpAgentsNames = Texto(json, "grp_agents_names"),
            InstancesNames = Texto(json, "instances_names"),
            GrpInstancesNames = Texto(json, "grp_instances_names"),
            ListId = Entero64(json, "list_id"),
            LeadId = Entero64(json, "lead_id"),
            LeadEncryptedId = Entero64(json, "lead_encrypted_id"),
            Attempts = (int?)Entero64(json, "attempts"),
            Tipificacion = tipificacion,
            Dispositions = dispositions,
            CamposExtraJson = extras.Count > 0 ? extras.ToJsonString() : null,
            DatosLead = datosLead,
        };
    }

    /// <summary>
    /// Algunas listas traen la fecha de carga en el nombre del campo (ref_str_PRED_28102026).
    /// La fecha solo indica de qué lista viene, así que todas caen en la misma columna (ref_str_PRED).
    /// Solo se quita si los 8 dígitos finales son una fecha válida DDMMAAAA.
    /// </summary>
    internal static string NombreColumna(string campo)
    {
        var coincidencia = SufijoFecha().Match(campo);
        return coincidencia.Success
            && DateTime.TryParseExact(coincidencia.Groups[2].Value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? coincidencia.Groups[1].Value
            : campo;
    }

    [GeneratedRegex(@"^(ref_str_.+)_(\d{8})$")]
    private static partial Regex SufijoFecha();

    /// <summary>
    /// "dispositions" viene así: [{"175981": [{"disp_id": 91751, "name": "...", "text": "...", ...}]}].
    /// Lo normal es una sola tipificación: va en columnas. Si trae más de una o una estructura distinta,
    /// también se regresa el arreglo completo para no perder nada.
    /// </summary>
    private static (Tipificacion? Tipificacion, string? Completo) LeerTipificacion(JsonElement json)
    {
        if (!json.TryGetProperty("dispositions", out var arreglo) || arreglo.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        try
        {
            var tipificaciones = new List<Tipificacion>();
            foreach (var objeto in arreglo.EnumerateArray())
            {
                foreach (var grupo in objeto.EnumerateObject())
                {
                    foreach (var d in grupo.Value.EnumerateArray())
                    {
                        tipificaciones.Add(new Tipificacion(
                            grupo.Name,
                            Entero64(d, "disp_id") ?? throw new JsonException("La tipificación no trae disp_id."),
                            Entero64(d, "list_disp_id"),
                            Texto(d, "name"),
                            Texto(d, "description"),
                            Texto(d, "code"),
                            Texto(d, "text"),
                            Texto(d, "type"),
                            Texto(d, "status"),
                            Texto(d, "input_type")));
                    }
                }
            }

            return tipificaciones.Count switch
            {
                0 => (null, null),
                1 => (tipificaciones[0], null),
                _ => (tipificaciones[0], arreglo.GetRawText()),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, arreglo.GetRawText());   // estructura que no conocemos: se guarda tal cual
        }
    }

    // Los vacíos se mandan tal cual: el SP los convierte a NULL (una sola regla, en la BD)
    private static string? Texto(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => valor.GetString(),
        _ => valor.GetRawText(),   // números o booleanos en un campo de texto
    };

    private static string? Texto(JsonElement json, string campo) =>
        json.TryGetProperty(campo, out var valor) ? Texto(valor) : null;

    private static long? Entero64(JsonElement json, string campo)
    {
        var texto = Texto(json, campo);
        return string.IsNullOrWhiteSpace(texto) ? null : long.Parse(texto, CultureInfo.InvariantCulture);
    }

    private static decimal? Decimal(JsonElement json, string campo)
    {
        var texto = Texto(json, campo);   // CCC manda los decimales como texto: "0.0000"
        return string.IsNullOrWhiteSpace(texto) ? null : decimal.Parse(texto, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static DateTime? Fecha(JsonElement json, string campo)
    {
        var texto = Texto(json, campo);
        return string.IsNullOrWhiteSpace(texto)
            ? null
            : DateTime.ParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture);
    }
}
