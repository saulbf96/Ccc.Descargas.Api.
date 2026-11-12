using System.Text.Json;
using Ccc.Descargas.Api.Seguridad;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Infrastructure.Ccc;
using Ccc.Descargas.Infrastructure.Reportes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Tests;

public class CccLlamadaMapperTests
{
    [Theory]
    [InlineData("ref_str_PRED_28102026", "ref_str_PRED")]
    [InlineData("ref_str_SMS_01112026", "ref_str_SMS")]
    [InlineData("ref_str_CLIENTE_12345678", "ref_str_CLIENTE_12345678")]   // 12345678 no es fecha (mes 34)
    [InlineData("ref_str_TEL_1", "ref_str_TEL_1")]
    [InlineData("ref_str_CURP", "ref_str_CURP")]
    public void Quita_la_fecha_del_nombre_solo_si_es_una_fecha_valida(string campo, string columna) =>
        Assert.Equal(columna, CccLlamadaMapper.NombreColumna(campo));

    private static Ccc.Descargas.Domain.Llamadas.Llamada Mapear(string extra) =>
        CccLlamadaMapper.Mapear(JsonDocument.Parse($$"""
            {
              "call_id": 123, "call_direction": "outbound", "campaign_id": 5, "agent": "Ana",
              "callerid_number": "0055", "called_number": "55", "answer_state": "ANSWERED",
              "duration_secs": 30, "duration_mins": "0.5000", "start_datetime": "2026-10-29 12:01:02",
              "answer_datetime": null, "ref_str_PRED_28102026": "suceptible 1", "ref_str_SALDO_TOTAL": ""
              {{extra}}
            }
            """).RootElement, 200929);

    [Fact]
    public void Mapea_campos_fijos_y_ref_str_con_fecha_a_su_columna()
    {
        var l = Mapear("");

        Assert.Equal(123, l.CallId);
        Assert.Equal("0055", l.CallerIdNumber);   // conserva ceros a la izquierda
        Assert.Equal(0.5m, l.DurationMins);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 1, 2), l.StartDatetime);
        Assert.Null(l.AnswerDatetime);
        Assert.Equal("suceptible 1", l.DatosLead["ref_str_PRED"]);
        Assert.Equal("", l.DatosLead["ref_str_SALDO_TOTAL"]);   // el SP lo convierte en NULL
        Assert.Null(l.CamposExtraJson);
    }

    [Fact]
    public void Un_campo_nuevo_sin_columna_va_a_campos_extra_y_no_se_pierde()
    {
        var l = Mapear(""", "ref_str_NUEVO_CAMPO": "x", "campo_nuevo_ccc": 7""");

        Assert.Contains("ref_str_NUEVO_CAMPO", l.CamposExtraJson);
        Assert.Contains("campo_nuevo_ccc", l.CamposExtraJson);
        Assert.DoesNotContain("ref_str_NUEVO_CAMPO", l.DatosLead.Keys);
    }

    [Fact]
    public void Una_tipificacion_va_en_columnas()
    {
        var l = Mapear(""", "dispositions": [{"175981": [{"disp_id": 91751, "list_disp_id": 14242, "name": "Cobranza", "text": "Llamar después", "input_type": "select"}]}]""");

        Assert.Equal("175981", l.Tipificacion!.Clave);
        Assert.Equal(91751, l.Tipificacion.DispId);
        Assert.Equal("Llamar después", l.Tipificacion.Text);
        Assert.Null(l.Dispositions);
    }

    [Fact]
    public void Varias_tipificaciones_guardan_tambien_el_arreglo_completo()
    {
        var l = Mapear(""", "dispositions": [{"1": [{"disp_id": 1}]}, {"2": [{"disp_id": 2}]}]""");

        Assert.Equal(1, l.Tipificacion!.DispId);
        Assert.NotNull(l.Dispositions);
    }

    [Fact]
    public void Una_tipificacion_con_formato_desconocido_se_guarda_tal_cual()
    {
        var l = Mapear(""", "dispositions": ["texto raro"]""");

        Assert.Null(l.Tipificacion);
        Assert.Equal("""["texto raro"]""", l.Dispositions);
    }
}

public class ReporteTxtTests
{
    [Fact]
    public async Task Escribe_en_la_carpeta_de_la_cuenta_un_archivo_por_dia_y_agrega_lineas()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "ccc-reportes-" + Guid.NewGuid());
        try
        {
            var reporte = new ReporteTxt(Options.Create(new ReportesOptions { Carpeta = carpeta }), new CuentasFake(Datos.Consu), TimeProvider.System);
            var hora = new DateTime(2026, 10, 30, 10, 0, 0);
            var ok = new RegistroTramo("2812", "CONSU", TipoDescarga.Llamadas, hora, hora.AddMinutes(15).AddSeconds(-1), 60,
                OrigenDescarga.Automatica, true, 8, 7876, 7876, 0, hora.AddMinutes(75), hora.AddMinutes(80), "12 tramo(s)");
            var error = ok with { Exitosa = false, Mensaje = "Intermitencia de red" };

            await reporte.EscribirTramoAsync(ok, default);
            await reporte.EscribirTramoAsync(error, default);

            var archivo = Path.Combine(carpeta, "CONSU_2812", $"CONSU_2812_{DateTime.Now:yyyy-MM-dd}.txt");
            var lineas = await File.ReadAllLinesAsync(archivo);
            Assert.StartsWith("Cuenta 2812", lineas[0]);
            Assert.Contains("OK       LLAMADAS 30/10 10:00:00-10:14:59  |  7,876 llamadas", lineas[2]);
            Assert.Contains("12 tramo(s)", lineas[2]);
            Assert.Contains("ERROR", lineas[3]);
            Assert.Contains("Intermitencia de red  |  se reintenta en el siguiente turno", lineas[3]);
        }
        finally
        {
            if (Directory.Exists(carpeta))
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }
    }
}

public class ApiKeyFilterTests
{
    private const string Clave = "una-clave-de-prueba-de-mas-de-32-caracteres!!";

    private static IActionResult? Probar(string? encabezado)
    {
        var http = new DefaultHttpContext();
        if (encabezado is not null)
        {
            http.Request.Headers[ApiKeyFilter.Encabezado] = encabezado;
        }

        var contexto = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        new ApiKeyFilter(Options.Create(new SeguridadOptions { ApiKey = Clave })).OnAuthorization(contexto);
        return contexto.Result;
    }

    [Fact]
    public void Sin_clave_responde_401() => Assert.IsType<UnauthorizedResult>(Probar(null));

    [Fact]
    public void Con_clave_incorrecta_responde_401() => Assert.IsType<UnauthorizedResult>(Probar("otra"));

    [Fact]
    public void Con_la_clave_correcta_deja_pasar() => Assert.Null(Probar(Clave));
}
