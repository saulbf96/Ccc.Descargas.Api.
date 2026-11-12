using System.Text.Json;
using Ccc.Descargas.Application.Agentes;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Infrastructure.Ccc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Tests;

public class CccAgenteMapperTests
{
    [Fact]
    public void GetTimes_columnas_fijas_pausas_por_tipo_y_lo_demas_en_extras()
    {
        // campos de 2812 recortados
        var t = CccAgenteMapper.MapearTiempo(JsonDocument.Parse("""
            {
              "agent_id": 146067, "date": "2026-11-10", "agent_username": "UNOAGENTE10", "agent_name": "DIANA",
              "login_datetime": "2026-11-10 08:42:43", "logout_datetime": "", "calls_handled": 188, "aht": "47.61",
              "status_pause_percent": "4", "status_pause_minutes": "0.2", "status_pause_baÑo_minutes": "10.6",
              "status_pause_baÑo_hhmmss": "00:10:33", "status_pause_retro_calidad_minutes": "1.6",
              "status_time_exceeded_pause_retro_minutes": "1.7", "status_oncall_minutes": "148.5"
            }
            """).RootElement);

        Assert.Equal(146067, t.AgentId);
        Assert.Equal(new DateOnly(2026, 11, 10), t.Date);
        Assert.Equal(new DateTime(2026, 11, 10, 8, 42, 43), t.LoginDatetime);
        Assert.Null(t.LogoutDatetime);   // "" = sigue conectado
        Assert.Equal(188, t.CallsHandled);
        Assert.Equal(47.61m, t.Aht);
        Assert.Equal(0.2m, t.StatusPauseMinutes);
        Assert.Equal(148.5m, t.StatusOncallMinutes);
        var pausas = JsonDocument.Parse(t.PausasJson!).RootElement;
        Assert.Equal(10.6m, pausas.GetProperty("baÑo").GetDecimal());
        Assert.Equal(1.6m, pausas.GetProperty("retro_calidad").GetDecimal());
        var extras = JsonDocument.Parse(t.CamposExtraJson!).RootElement;
        Assert.Equal("00:10:33", extras.GetProperty("status_pause_baÑo_hhmmss").GetString());
        Assert.True(extras.TryGetProperty("status_time_exceeded_pause_retro_minutes", out _));
    }

    [Fact]
    public void GetStatus_ref_str_al_lead_y_un_valor_raro_no_se_pierde()
    {
        var foto = new DateTime(2026, 11, 10, 13, 16, 0);
        var e = CccAgenteMapper.MapearEstatus(JsonDocument.Parse("""
            {
              "USE_DB_RTX": null, "login_id": 107801543, "user_id": 146067, "call_id": 92803113603,
              "agent_status": "ONCALL", "agent_status_datetime": "2026-11-10 13:15:52", "agent_status_time": "no-es-numero",
              "call_state": "ANSWERED", "campaign_id": 4775038, "call_destination": "5614259966",
              "ref_str_TEL_1": "5614259966", "ref_str_CURP": "XXXX", "campo_nuevo": 7
            }
            """).RootElement, 146067, foto);

        Assert.Equal(foto, e.FechaFoto);
        Assert.Equal("ONCALL", e.AgentStatus);
        Assert.Equal(92803113603, e.CallId);
        Assert.Null(e.UseDbRtx);
        Assert.Null(e.AgentStatusTime);
        var lead = JsonDocument.Parse(e.DatosLeadJson!).RootElement;
        Assert.Equal("XXXX", lead.GetProperty("ref_str_CURP").GetString());
        var extras = JsonDocument.Parse(e.CamposExtraJson!).RootElement;
        Assert.Equal(7, extras.GetProperty("campo_nuevo").GetInt32());
        Assert.Equal("no-es-numero", extras.GetProperty("agent_status_time").GetString());
    }
}

public class DescargarAgentesServiceTests
{
    private static readonly DateTime Ahora = new(2026, 11, 10, 13, 16, 0);

    private static TiempoAgente Agente(long id, string usuario) => new() { AgentId = id, Date = new DateOnly(2026, 11, 10), AgentUsername = usuario };

    private static (DescargarAgentesService, AgentesFake, BitacoraFake) Crear(CccFake ccc, DateTime? ahora = null)
    {
        var agentes = new AgentesFake();
        var bitacora = new BitacoraFake();
        var servicio = new DescargarAgentesService(new CuentasFake(Datos.Consu), ccc, agentes, bitacora, new ReporteFake(),
            Options.Create(new DescargaOptions()), new RelojFijo(ahora ?? Ahora), NullLogger<DescargarAgentesService>.Instance);
        return (servicio, agentes, bitacora);
    }

    [Fact]
    public async Task La_foto_guarda_los_tiempos_y_una_foto_por_agente_con_su_usuario()
    {
        var ccc = new CccFake
        {
            Tiempos = [Agente(1, "UNOAGENTE1"), Agente(2, "UNOAGENTE2"), Agente(3, "UNOAGENTE3")],
            // El agente 3 ya no está conectado: CCC no regresa estado
            Estatus = id => id == 3 ? null : new EstatusAgente { FechaFoto = default, UserId = id, AgentStatus = "PAUSE" },
        };
        var (servicio, agentes, bitacora) = Crear(ccc);

        var (tiempos, estatus) = await servicio.TomarFotoAsync(Datos.Consu, OrigenDescarga.Automatica, default);

        Assert.True(tiempos.Exitosa);
        Assert.True(estatus!.Exitosa);
        Assert.Equal(3, agentes.Tiempos.Count);
        Assert.Equal(["UNOAGENTE1", "UNOAGENTE2"], agentes.Fotos.Select(f => f.AgentUsername));
        Assert.All(agentes.Fotos, f => Assert.Equal(Ahora, f.FechaFoto));
        Assert.Equal(2, estatus.Registros);
        Assert.All(bitacora.Registros, r => Assert.False(r.MoverAvance));
    }

    [Fact]
    public async Task La_primera_vez_no_cierra_dias_anteriores_y_empieza_desde_hoy()
    {
        var (servicio, _, bitacora) = Crear(new CccFake(), new DateTime(2026, 11, 11, 15, 0, 0));   // arranca un miércoles

        Assert.Null(await servicio.DiaPorCerrarAsync(Datos.Consu, default));   // no cierra el martes
        Assert.Equal(new DateTime(2026, 11, 11), bitacora.Avance[("2812", TipoDescarga.TiemposAgentes)].DescargadoHasta);
    }

    [Fact]
    public async Task Cierra_ayer_despues_de_la_hora_de_cierre_y_luego_ya_no()
    {
        var (servicio, _, bitacora) = Crear(new CccFake(), new DateTime(2026, 11, 10, 1, 10, 0));
        bitacora.Avance[("2812", TipoDescarga.TiemposAgentes)] = new AvanceDescarga(new DateTime(2026, 11, 9), null, 0, null, null);

        var dia = await servicio.DiaPorCerrarAsync(Datos.Consu, default);
        Assert.Equal(new DateOnly(2026, 11, 9), dia);

        await servicio.CerrarDiaAsync(Datos.Consu, dia!.Value, OrigenDescarga.Automatica, default);

        Assert.Equal(new DateTime(2026, 11, 10), bitacora.Avance[("2812", TipoDescarga.TiemposAgentes)].DescargadoHasta);
        Assert.Null(await servicio.DiaPorCerrarAsync(Datos.Consu, default));
    }

    [Fact]
    public async Task Antes_de_la_hora_de_cierre_no_cierra_nada()
    {
        var (servicio, _, _) = Crear(new CccFake(), new DateTime(2026, 11, 10, 0, 50, 0));

        Assert.Null(await servicio.DiaPorCerrarAsync(Datos.Consu, default));
    }
}
