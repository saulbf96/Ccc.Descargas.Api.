using Ccc.Descargas.Application.Llamadas;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Tests;

public class DescargarLlamadasServiceTests
{
    private static readonly DateTime Hora10 = new(2026, 10, 30, 10, 0, 0);

    private static (DescargarLlamadasService Servicio, LlamadasFake Llamadas, BitacoraFake Bitacora, ReporteFake Reporte) Crear(
        CccFake ccc, BitacoraFake? bitacora = null, DescargaOptions? opciones = null)
    {
        var llamadas = new LlamadasFake();
        var reporte = new ReporteFake();
        bitacora ??= new BitacoraFake();
        var servicio = new DescargarLlamadasService(new CuentasFake(Datos.Consu), ccc, llamadas, bitacora, reporte,
            Options.Create(opciones ?? new DescargaOptions()), new RelojFijo(Hora10.AddHours(5)), NullLogger<DescargarLlamadasService>.Instance);
        return (servicio, llamadas, bitacora, reporte);
    }

    private static void ConAvance(BitacoraFake bitacora, DateTime desde, int? tamano = null) =>
        bitacora.Avance[("2812", TipoDescarga.Llamadas)] = new AvanceDescarga(desde, tamano, 0, null, null);

    [Fact]
    public async Task Un_tramo_recorre_todas_sus_paginas_hasta_una_incompleta()
    {
        var ccc = new CccFake(() => Datos.Pagina(1000, 1), () => Datos.Pagina(1000, 1001), () => Datos.Pagina(250, 2001));
        var (servicio, llamadas, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(1), default);

        Assert.Equal([1, 2, 3], ccc.Pedidos.Select(p => p.Pagina));
        Assert.All(ccc.Pedidos, p => Assert.Equal((Hora10, Hora10.AddSeconds(59)), (p.Desde, p.Hasta)));   // 10:00:00-10:00:59
        Assert.Equal(2250, llamadas.Guardadas.Count);
        Assert.Equal(1, turno.Tramos);
        Assert.Null(turno.Error);
        Assert.Equal(Hora10.AddMinutes(1), turno.DescargadoHasta);
        Assert.Equal(Hora10.AddMinutes(1), bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
    }

    [Fact]
    public async Task Avanza_desde_el_marcador_sin_huecos_ni_encimarse_hasta_el_limite()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        ConAvance(bitacora, Hora10);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(10), default);

        var tramos = bitacora.Registros.Select(r => r.Registro).ToList();
        Assert.Equal(Hora10, tramos[0].Desde);
        for (var i = 1; i < tramos.Count; i++)
        {
            Assert.Equal(tramos[i - 1].Hasta.AddSeconds(1), tramos[i].Desde);   // contiguos
        }
        Assert.Equal(Hora10.AddMinutes(10).AddSeconds(-1), tramos[^1].Hasta);   // no pasa del límite
        Assert.Equal(TimeSpan.Zero, turno.Atraso);
    }

    [Fact]
    public void El_tramo_apunta_a_una_sola_pagina_proporcional_a_lo_que_trajo()
    {
        var o = new DescargaOptions();   // objetivo 800 llamadas, 5 s a 900 s
        Assert.Equal(120, TramosAdaptativos.Siguiente(60, 0, 1, o));          // vacío: el doble
        Assert.Equal(120, TramosAdaptativos.Siguiente(60, 100, 1, o));        // pocas: crece máximo al doble
        Assert.Equal(900, TramosAdaptativos.Siguiente(900, 10, 1, o));        // no pasa del máximo
        Assert.Equal(60, TramosAdaptativos.Siguiente(60, 800, 1, o));         // justo el objetivo: igual
        Assert.Equal(30, TramosAdaptativos.Siguiente(60, 1600, 2, o));        // el doble del objetivo: la mitad
        Assert.Equal(15, TramosAdaptativos.Siguiente(60, 9000, 9, o));        // pico: se achica máximo a la cuarta parte
        Assert.Equal(5, TramosAdaptativos.Siguiente(10, 9000, 9, o));         // no baja del mínimo
        Assert.Equal(60, TramosAdaptativos.Inicial(null, o));
        Assert.Equal(900, TramosAdaptativos.Inicial(5000, o));
        Assert.Equal(30, TramosAdaptativos.Reducido(60, o));
        Assert.Null(TramosAdaptativos.Reducido(5, o));                        // ya en el mínimo
    }

    [Fact]
    public async Task Si_CCC_tarda_demasiado_reintenta_el_mismo_inicio_con_la_mitad()
    {
        var ccc = new CccFake(() => throw new TaskCanceledException("timeout"), () => Datos.Pagina(10));
        var (servicio, llamadas, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(10), default);

        Assert.Null(turno.Error);
        Assert.Equal(Hora10.AddSeconds(59), ccc.Pedidos[0].Hasta);   // 60 s: timeout
        Assert.Equal(Hora10, ccc.Pedidos[1].Desde);                  // mismo inicio...
        Assert.Equal(Hora10.AddSeconds(29), ccc.Pedidos[1].Hasta);   // ...a la mitad
        Assert.False(bitacora.Registros[0].Registro.Exitosa);        // el intento fallido queda en la bitácora
        Assert.True(bitacora.Registros[1].Registro.Exitosa);
        Assert.Equal(10, llamadas.Guardadas.Count);
        Assert.Equal(0, bitacora.Avance[("2812", TipoDescarga.Llamadas)].ErroresSeguidos);
    }

    [Fact]
    public async Task Si_CCC_rechaza_credenciales_no_reintenta_achicando()
    {
        var ccc = new CccFake(() => throw new CccApiException(401, "x"));
        var (servicio, _, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(10), default);

        Assert.NotNull(turno.Error);
        Assert.Single(ccc.Pedidos);
    }

    [Fact]
    public async Task La_primera_vez_empieza_a_las_00_de_ese_dia_y_no_baja_dias_anteriores()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());

        await servicio.AvanzarAsync(Datos.Consu, new DateTime(2026, 10, 30, 13, 5, 30), default);

        Assert.Equal(new DateTime(2026, 10, 30, 0, 0, 0), bitacora.Registros[0].Registro.Desde);
    }

    [Fact]
    public async Task Si_estuvo_apagada_dias_sigue_desde_su_marcador_sin_limite()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        ConAvance(bitacora, Hora10.AddDays(-2), tamano: 900);

        await servicio.AvanzarAsync(Datos.Consu, Hora10, default);

        Assert.Equal(Hora10.AddDays(-2), bitacora.Registros[0].Registro.Desde);
    }

    [Fact]
    public async Task El_repaso_pide_el_dia_completo_sin_mover_el_marcador()
    {
        var ccc = new CccFake(() => Datos.Pagina(10));
        var (servicio, llamadas, bitacora, reporte) = Crear(ccc);
        var hoy = Hora10.Date;
        ConAvance(bitacora, hoy.AddMinutes(45));   // la normal ya pasó la medianoche

        var repaso = await servicio.RepasarDiaAsync(Datos.Consu, DateOnly.FromDateTime(hoy.AddDays(-1)), default);

        Assert.NotNull(repaso);
        Assert.Equal(0, repaso.TramosConError);
        Assert.Equal(hoy.AddDays(-1), repaso.Tramos[0].Desde);
        Assert.Equal(hoy.AddSeconds(-1), repaso.Tramos[^1].Hasta);   // 23:59:59
        for (var i = 1; i < repaso.Tramos.Count; i++)
        {
            Assert.Equal(repaso.Tramos[i - 1].Hasta.AddSeconds(1), repaso.Tramos[i].Desde);   // sin huecos
        }
        Assert.Equal(10, llamadas.Guardadas.Count);
        Assert.All(bitacora.Registros, r => Assert.False(r.MoverAvance));
        Assert.All(bitacora.Registros, r => Assert.StartsWith("Repaso del", r.Registro.Mensaje));
        Assert.Equal(hoy.AddMinutes(45), bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
        Assert.Single(reporte.Tramos);   // una línea por todo el repaso
    }

    [Fact]
    public async Task El_repaso_espera_si_la_descarga_normal_no_ha_pasado_la_medianoche()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        var hoy = Hora10.Date;
        ConAvance(bitacora, hoy.AddMinutes(-30));   // va en las 23:30 de ayer

        Assert.Null(await servicio.RepasarDiaAsync(Datos.Consu, DateOnly.FromDateTime(hoy.AddDays(-1)), default));
        Assert.Empty(bitacora.Registros);
    }

    [Theory]
    [MemberData(nameof(Fallas))]
    public async Task Un_tramo_con_error_no_mueve_el_avance_y_explica_el_motivo(Exception falla, string motivoEsperado)
    {
        // sin reintentos: si falla el avance no se mueve
        var (servicio, _, bitacora, reporte) = Crear(new CccFake(() => Datos.Pagina(1000), () => throw falla),
            opciones: new DescargaOptions { ReintentosReduciendo = 0 });
        ConAvance(bitacora, Hora10);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(30), default);

        Assert.NotNull(turno.Error);
        Assert.Contains(motivoEsperado, turno.Error.Mensaje);
        Assert.Contains("(página 2)", turno.Error.Mensaje);
        Assert.Equal(0, turno.Tramos);
        Assert.Equal(Hora10, turno.DescargadoHasta);
        var avance = bitacora.Avance[("2812", TipoDescarga.Llamadas)];
        Assert.Equal(Hora10, avance.DescargadoHasta);   // se vuelve a pedir el mismo tramo
        Assert.Equal(1, avance.ErroresSeguidos);
        Assert.Contains(motivoEsperado, Assert.Single(reporte.Tramos).Mensaje);
    }

    public static TheoryData<Exception, string> Fallas() => new()
    {
        { new HttpRequestException("reset"), "Intermitencia de red" },
        { new TaskCanceledException("timeout"), "tardó demasiado" },
        { new CccApiException(401, "x"), "credenciales" },
        { new CccApiException(400, "x", ocupado: true), "ocupado" },
        { new CccApiException(503, "x"), "Falla del lado de CCC (HTTP 503)" },
        { new ErrorBaseDatosException("no hay conexión con el servidor de BD", new Exception()), "base de datos" },
    };

    [Fact]
    public async Task Despues_de_un_error_el_siguiente_turno_repite_el_mismo_tramo()
    {
        var ccc = new CccFake(() => throw new HttpRequestException("reset"), () => Datos.Pagina(10));
        var (servicio, llamadas, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10);

        await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(1), default);
        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(1), default);

        Assert.Equal(ccc.Pedidos[0].Desde, ccc.Pedidos[1].Desde);
        Assert.Null(turno.Error);
        Assert.Equal(10, llamadas.Guardadas.Count);
        Assert.Equal(0, bitacora.Avance[("2812", TipoDescarga.Llamadas)].ErroresSeguidos);
    }

    [Fact]
    public async Task Si_la_bitacora_falla_el_turno_se_detiene_con_error_de_base_de_datos()
    {
        var (servicio, _, _, _) = Crear(new CccFake(() => Datos.Pagina(5)), new BitacoraFake(falla: true));

        await Assert.ThrowsAnyAsync<Exception>(() => servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(1), default));
    }

    [Fact]
    public async Task La_manual_baja_el_rango_por_tramos_sigue_tras_un_error_y_no_mueve_el_avance()
    {
        var ccc = new CccFake(() => throw new HttpRequestException("reset"), () => Datos.Pagina(10));
        var (servicio, _, bitacora, _) = Crear(ccc);

        var resultado = await servicio.DescargarRangoAsync("2812", Hora10, Hora10.AddMinutes(3), default);

        Assert.Equal(EstadoTramo.Error, resultado!.Tramos[0].Estado);
        Assert.Equal(EstadoTramo.Completado, resultado.Tramos[1].Estado);
        Assert.Equal(1, resultado.TramosConError);
        Assert.Equal(Hora10.AddMinutes(3).AddSeconds(-1), resultado.Tramos[^1].Hasta);
        Assert.All(bitacora.Registros, r => Assert.False(r.MoverAvance));
        Assert.Empty(bitacora.Avance);
    }

    [Fact]
    public async Task Cuenta_que_no_existe_regresa_null()
    {
        var (servicio, _, _, _) = Crear(new CccFake());

        Assert.Null(await servicio.DescargarRangoAsync("9999", Hora10, Hora10.AddHours(1), default));
    }
}
