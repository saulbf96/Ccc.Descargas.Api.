using System.Data;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Llamadas;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Persistencia;

// guarda llamadas con el SP de la cuenta (@OPCION 1)
internal sealed class LlamadaRepository(IOptions<BaseDatosOptions> options) : ILlamadaRepository
{
    public async Task<(int Insertadas, int Actualizadas)> GuardarAsync(IReadOnlyList<Llamada> llamadas, CancellationToken cancellationToken)
    {
        var clientId = llamadas[0].ClientId;
        if (llamadas.Any(l => l.ClientId != clientId))
        {
            throw new InvalidOperationException("Un lote solo puede traer llamadas de una cuenta.");
        }

        // nombre del SP de la lista fija de cuentas
        var tablaCuenta = ProcedimientoCuenta.Tabla(clientId);

        return await ProcedimientoCuenta.EjecutarAsync(options.Value, tablaCuenta, ProcedimientoCuenta.GuardarLlamadas,
            p => p.Add(new SqlParameter("@Llamadas", SqlDbType.Structured)
            {
                TypeName = tablaCuenta.TipoLlamadas,
                Value = CrearTablaLlamadas(llamadas, tablaCuenta.ColumnasLead),
            }),
            ProcedimientoCuenta.LeerConteosAsync, cancellationToken);
    }

    // mismo orden que TT_LLAMADAS_CCC_<cuenta>
    private static DataTable CrearTablaLlamadas(IReadOnlyList<Llamada> llamadas, IReadOnlyList<string> columnasLead)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("call_id", typeof(long));
        tabla.Columns.Add("call_direction", typeof(string));
        tabla.Columns.Add("campaign_id", typeof(long));
        tabla.Columns.Add("campaign_type", typeof(string));
        tabla.Columns.Add("campaign_name", typeof(string));
        tabla.Columns.Add("agent", typeof(string));
        tabla.Columns.Add("user_id", typeof(long));
        tabla.Columns.Add("callerid_number", typeof(string));
        tabla.Columns.Add("called_number", typeof(string));
        tabla.Columns.Add("destination", typeof(string));
        tabla.Columns.Add("answer_state", typeof(string));
        tabla.Columns.Add("amd_status", typeof(string));
        tabla.Columns.Add("hangup_reason", typeof(string));
        tabla.Columns.Add("hangup_code", typeof(int));
        tabla.Columns.Add("hangup_code_sip", typeof(int));
        tabla.Columns.Add("duration_secs", typeof(int));
        tabla.Columns.Add("duration_mins", typeof(decimal));
        tabla.Columns.Add("bill_time", typeof(decimal));
        tabla.Columns.Add("bill_rate", typeof(decimal));
        tabla.Columns.Add("bill_cost", typeof(decimal));
        tabla.Columns.Add("start_datetime", typeof(DateTime));
        tabla.Columns.Add("answer_datetime", typeof(DateTime));
        tabla.Columns.Add("hangup_datetime", typeof(DateTime));
        tabla.Columns.Add("account_nr", typeof(int));
        tabla.Columns.Add("tags", typeof(string));
        tabla.Columns.Add("grp_agents_ids", typeof(string));
        tabla.Columns.Add("grp_agents_names", typeof(string));
        tabla.Columns.Add("instances_names", typeof(string));
        tabla.Columns.Add("grp_instances_names", typeof(string));
        tabla.Columns.Add("list_id", typeof(long));
        tabla.Columns.Add("lead_id", typeof(long));
        tabla.Columns.Add("lead_encrypted_id", typeof(long));
        tabla.Columns.Add("attempts", typeof(int));
        tabla.Columns.Add("dispositions_key", typeof(string));
        tabla.Columns.Add("disp_id", typeof(long));
        tabla.Columns.Add("list_disp_id", typeof(long));
        tabla.Columns.Add("disp_name", typeof(string));
        tabla.Columns.Add("disp_description", typeof(string));
        tabla.Columns.Add("disp_code", typeof(string));
        tabla.Columns.Add("disp_text", typeof(string));
        tabla.Columns.Add("disp_type", typeof(string));
        tabla.Columns.Add("disp_status", typeof(string));
        tabla.Columns.Add("disp_input_type", typeof(string));
        tabla.Columns.Add("dispositions", typeof(string));
        foreach (var columna in columnasLead)
        {
            tabla.Columns.Add(columna, typeof(string));
        }
        tabla.Columns.Add("CAMPOS_EXTRA", typeof(string));

        foreach (var l in llamadas)
        {
            var fila = tabla.NewRow();
            fila["call_id"] = l.CallId;
            fila["call_direction"] = l.CallDirection;
            fila["campaign_id"] = Nulo(l.CampaignId);
            fila["campaign_type"] = Nulo(l.CampaignType);
            fila["campaign_name"] = Nulo(l.CampaignName);
            fila["agent"] = Nulo(l.Agent);
            fila["user_id"] = Nulo(l.UserId);
            fila["callerid_number"] = l.CallerIdNumber;
            fila["called_number"] = l.CalledNumber;
            fila["destination"] = Nulo(l.Destination);
            fila["answer_state"] = l.AnswerState;
            fila["amd_status"] = Nulo(l.AmdStatus);
            fila["hangup_reason"] = Nulo(l.HangupReason);
            fila["hangup_code"] = Nulo(l.HangupCode);
            fila["hangup_code_sip"] = Nulo(l.HangupCodeSip);
            fila["duration_secs"] = l.DurationSecs;
            fila["duration_mins"] = Nulo(l.DurationMins);
            fila["bill_time"] = Nulo(l.BillTime);
            fila["bill_rate"] = Nulo(l.BillRate);
            fila["bill_cost"] = Nulo(l.BillCost);
            fila["start_datetime"] = l.StartDatetime;
            fila["answer_datetime"] = Nulo(l.AnswerDatetime);
            fila["hangup_datetime"] = Nulo(l.HangupDatetime);
            fila["account_nr"] = Nulo(l.AccountNr);
            fila["tags"] = Nulo(l.Tags);
            fila["grp_agents_ids"] = Nulo(l.GrpAgentsIds);
            fila["grp_agents_names"] = Nulo(l.GrpAgentsNames);
            fila["instances_names"] = Nulo(l.InstancesNames);
            fila["grp_instances_names"] = Nulo(l.GrpInstancesNames);
            fila["list_id"] = Nulo(l.ListId);
            fila["lead_id"] = Nulo(l.LeadId);
            fila["lead_encrypted_id"] = Nulo(l.LeadEncryptedId);
            fila["attempts"] = Nulo(l.Attempts);

            if (l.Tipificacion is { } t)
            {
                fila["dispositions_key"] = t.Clave;
                fila["disp_id"] = t.DispId;
                fila["list_disp_id"] = Nulo(t.ListDispId);
                fila["disp_name"] = Nulo(t.Name);
                fila["disp_description"] = Nulo(t.Description);
                fila["disp_code"] = Nulo(t.Code);
                fila["disp_text"] = Nulo(t.Text);
                fila["disp_type"] = Nulo(t.Type);
                fila["disp_status"] = Nulo(t.Status);
                fila["disp_input_type"] = Nulo(t.InputType);
            }
            fila["dispositions"] = Nulo(l.Dispositions);

            foreach (var (columna, valor) in l.DatosLead)
            {
                fila[columna] = Nulo(valor);
            }
            fila["CAMPOS_EXTRA"] = Nulo(l.CamposExtraJson);
            tabla.Rows.Add(fila);
        }

        return tabla;
    }

    private static object Nulo(object? valor) => ProcedimientoCuenta.Nulo(valor);
}
