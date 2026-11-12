using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Cuentas;

namespace Ccc.Descargas.Tests;

public class ResumenDescargasTests
{
    private static readonly DateTime Ahora = new(2026, 11, 10, 7, 0, 0);
    private static readonly DateTime Limite = Ahora.AddMinutes(-15);

    private static ResumenDescargas.Correo Armar(params (CuentaCcc, ResumenCuenta)[] cuentas) =>
        ResumenDescargas.Armar("Inicio del día", cuentas, Ahora.Date, Ahora, Ahora, Limite, TimeSpan.FromMinutes(60), "SERVIDOR");

    private static ResumenCuenta Resumen(DateTime? llamadasHasta, int errores = 0) => new(
        [
            new ResumenTipo(TipoDescarga.Llamadas, 400, errores, 12345, Ahora, errores > 0 ? "Intermitencia de red" : null),
            new ResumenTipo(TipoDescarga.EstatusAgentes, 80, 0, 1600, Ahora, null),
        ],
        llamadasHasta is null
            ? new Dictionary<TipoDescarga, AvanceDescarga>()
            : new Dictionary<TipoDescarga, AvanceDescarga>
            {
                [TipoDescarga.Llamadas] = new(llamadasHasta.Value, 60, 0, null, null),
                [TipoDescarga.TiemposAgentes] = new(Ahora.Date, null, 0, null, null),
            });

    [Fact]
    public void Todo_al_dia_es_OK()
    {
        var correo = Armar((Datos.Consu, Resumen(Limite)));

        Assert.False(correo.HayProblemas);
        Assert.StartsWith("[OK]", correo.Asunto);
        Assert.Contains("(al día)", correo.Cuerpo);
        Assert.Contains("12,345 llamadas", correo.Cuerpo);
        Assert.Contains("1,600 fotos de agentes", correo.Cuerpo);
        Assert.Contains("tiempos cerrados hasta el 09/11", correo.Cuerpo);
    }

    [Fact]
    public void Una_cuenta_muy_atrasada_o_con_errores_es_REVISAR()
    {
        var correo = Armar((Datos.Consu, Resumen(Limite.AddHours(-3))), (Datos.Isb, Resumen(Limite, errores: 2)));

        Assert.True(correo.HayProblemas);
        Assert.StartsWith("[REVISAR]", correo.Asunto);
        Assert.Contains("2812: va 3h 00m atrasada", correo.Cuerpo);
        Assert.Contains("200929 LLAMADAS: 2 con error. Último: Intermitencia de red", correo.Cuerpo);
    }

    [Fact]
    public void Una_cuenta_sin_descargas_es_REVISAR()
    {
        var correo = Armar((Datos.Consu, Resumen(null)));

        Assert.True(correo.HayProblemas);
        Assert.Contains("todavía sin descargas registradas", correo.Cuerpo);
    }
}
