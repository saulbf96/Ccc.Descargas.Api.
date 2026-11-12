using System.Data;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Domain.Agentes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using static Ccc.Descargas.Infrastructure.Persistencia.ProcedimientoCuenta;

namespace Ccc.Descargas.Infrastructure.Persistencia;

// guarda fotos (@OPCION 2) y tiempos (@OPCION 3)
internal sealed class AgenteRepository(IOptions<BaseDatosOptions> options) : IAgenteRepository
{
    public async Task<int> GuardarEstatusAsync(string clientId, IReadOnlyList<EstatusAgente> fotos, CancellationToken cancellationToken)
    {
        var (insertadas, _) = await EjecutarAsync(options.Value, Tabla(clientId), GuardarEstatus,
            p => p.Add(new SqlParameter("@Estatus", SqlDbType.Structured)
            {
                TypeName = TablaCuenta.TipoEstatus,
                Value = CrearTablaEstatus(fotos),
            }),
            LeerConteosAsync, cancellationToken);
        return insertadas;
    }

    public Task<(int Insertadas, int Actualizadas)> GuardarTiemposAsync(
        string clientId, IReadOnlyList<TiempoAgente> tiempos, CancellationToken cancellationToken) =>
        EjecutarAsync(options.Value, Tabla(clientId), GuardarTiempos,
            p => p.Add(new SqlParameter("@Tiempos", SqlDbType.Structured)
            {
                TypeName = TablaCuenta.TipoTiempos,
                Value = CrearTablaTiempos(tiempos),
            }),
            LeerConteosAsync, cancellationToken);

    // mismo orden que TT_ESTATUS_AGENTES_CCC
    internal static DataTable CrearTablaEstatus(IReadOnlyList<EstatusAgente> fotos)
    {
        var t = new DataTable();
        t.Columns.Add("FECHA_FOTO", typeof(DateTime));
        t.Columns.Add("user_id", typeof(long));
        t.Columns.Add("agent_username", typeof(string));
        t.Columns.Add("agent_name", typeof(string));
        t.Columns.Add("login_id", typeof(long));
        t.Columns.Add("agent_status", typeof(string));
        t.Columns.Add("agent_status_datetime", typeof(DateTime));
        t.Columns.Add("agent_status_time", typeof(int));
        t.Columns.Add("call_id", typeof(long));
        t.Columns.Add("call_state", typeof(string));
        t.Columns.Add("call_callerid_number", typeof(string));
        t.Columns.Add("call_destination", typeof(string));
        t.Columns.Add("campaign_id", typeof(long));
        t.Columns.Add("lead_id", typeof(long));
        t.Columns.Add("list_id", typeof(long));
        t.Columns.Add("USE_DB_RTX", typeof(string));
        t.Columns.Add("DATOS_LEAD", typeof(string));
        t.Columns.Add("CAMPOS_EXTRA", typeof(string));

        // un agente una vez por foto
        foreach (var f in fotos.DistinctBy(f => (f.UserId, f.FechaFoto)))
        {
            t.Rows.Add(f.FechaFoto, f.UserId, Nulo(f.AgentUsername), Nulo(f.AgentName), Nulo(f.LoginId), Nulo(f.AgentStatus),
                Nulo(f.AgentStatusDatetime), Nulo(f.AgentStatusTime), Nulo(f.CallId), Nulo(f.CallState), Nulo(f.CallCallerIdNumber),
                Nulo(f.CallDestination), Nulo(f.CampaignId), Nulo(f.LeadId), Nulo(f.ListId), Nulo(f.UseDbRtx),
                Nulo(f.DatosLeadJson), Nulo(f.CamposExtraJson));
        }

        return t;
    }

    // mismo orden que TT_TIEMPOS_AGENTES_CCC
    internal static DataTable CrearTablaTiempos(IReadOnlyList<TiempoAgente> tiempos)
    {
        var t = new DataTable();
        t.Columns.Add("agent_id", typeof(long));
        t.Columns.Add("date", typeof(DateTime));
        t.Columns.Add("agent_username", typeof(string));
        t.Columns.Add("agent_name", typeof(string));
        t.Columns.Add("login_datetime", typeof(DateTime));
        t.Columns.Add("logout_datetime", typeof(DateTime));
        t.Columns.Add("calls_handled", typeof(int));
        t.Columns.Add("calls_hungup", typeof(int));
        t.Columns.Add("calls_transfer", typeof(int));
        foreach (var c in new[]
                 {
                     "aht", "status_avail_percent", "status_oncall_percent", "status_aftercall_percent", "status_pause_percent",
                     "status_others_percent", "total_time_minutes", "status_online_minutes", "status_avail_minutes",
                     "status_oncall_minutes", "status_aftercall_minutes", "status_call_manual_minutes",
                     "status_call_conference_minutes", "status_pause_minutes", "status_monitoring_minutes",
                     "status_wait_campaign_minutes",
                 })
        {
            t.Columns.Add(c, typeof(decimal));
        }
        t.Columns.Add("PAUSAS", typeof(string));
        t.Columns.Add("CAMPOS_EXTRA", typeof(string));

        foreach (var a in tiempos.DistinctBy(a => (a.AgentId, a.Date)))
        {
            t.Rows.Add(a.AgentId, a.Date.ToDateTime(TimeOnly.MinValue), Nulo(a.AgentUsername), Nulo(a.AgentName),
                Nulo(a.LoginDatetime), Nulo(a.LogoutDatetime), Nulo(a.CallsHandled), Nulo(a.CallsHungup), Nulo(a.CallsTransfer),
                Nulo(a.Aht), Nulo(a.StatusAvailPercent), Nulo(a.StatusOncallPercent), Nulo(a.StatusAftercallPercent),
                Nulo(a.StatusPausePercent), Nulo(a.StatusOthersPercent), Nulo(a.TotalTimeMinutes), Nulo(a.StatusOnlineMinutes),
                Nulo(a.StatusAvailMinutes), Nulo(a.StatusOncallMinutes), Nulo(a.StatusAftercallMinutes),
                Nulo(a.StatusCallManualMinutes), Nulo(a.StatusCallConferenceMinutes), Nulo(a.StatusPauseMinutes),
                Nulo(a.StatusMonitoringMinutes), Nulo(a.StatusWaitCampaignMinutes), Nulo(a.PausasJson), Nulo(a.CamposExtraJson));
        }

        return t;
    }
}
