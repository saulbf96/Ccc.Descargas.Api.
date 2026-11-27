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
    public async Task En_el_tramo_minimo_recorre_todas_sus_paginas_hasta_una_incompleta()
    {
        var ccc = new CccFake(() => Datos.Pagina(1000, 1), () => Datos.Pagina(1000, 1001), () => Datos.Pagina(250, 2001));
        var (servicio, llamadas, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10, tamano: 5);   // ya no se puede achicar

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddSeconds(5), default);

        Assert.Equal([1, 2, 3], ccc.Pedidos.Select(p => p.Pagina));
        Assert.All(ccc.Pedidos, p => Assert.Equal((Hora10, Hora10.AddSeconds(4)), (p.Desde, p.Hasta)));   // 10:00:00-10:00:04
        Assert.Equal(2250, llamadas.Guardadas.Count);
        Assert.Equal(1, turno.Tramos);
        Assert.Null(turno.Error);
        Assert.Equal(Hora10.AddSeconds(5), turno.DescargadoHasta);
        Assert.Equal(Hora10.AddSeconds(5), bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
    }

    [Fact]
    public async Task Si_la_primera_pagina_sale_llena_pide_el_mismo_inicio_a_la_mitad_sin_guardar_ni_marcar_error()
    {
        var ccc = new CccFake(() => Datos.Pagina(1000, 1), () => Datos.Pagina(1000, 1), () => Datos.Pagina(300, 1));
        var (servicio, llamadas, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(1), default);

        // 60 s y 30 s llenan la página; 15 s cabe
        Assert.Equal([59, 29, 14], ccc.Pedidos.Take(3).Select(p => (int)(p.Hasta - Hora10).TotalSeconds));
        Assert.All(ccc.Pedidos.Take(3), p => Assert.Equal((Hora10, 1), (p.Desde, p.Pagina)));
        Assert.Equal(300, llamadas.Guardadas.Count);   // las páginas llenas no se guardan
        Assert.All(bitacora.Registros, r => Assert.True(r.Registro.Exitosa));
        Assert.Equal(15, bitacora.Registros[0].Registro.TamanoTramoSegundos);
        Assert.Equal(Hora10.AddSeconds(15), bitacora.Registros[1].Registro.Desde);   // sigue sin huecos
        Assert.Null(turno.Error);
        Assert.Equal(TimeSpan.Zero, turno.Atraso);
    }

    [Fact]
    public async Task Pide_el_siguiente_tramo_mientras_se_guarda_el_anterior_y_registra_en_orden()
    {
        var guardados = new GuardadoControlado();
        var ccc = new CccFake(() => Datos.Pagina(300, 1), () => Datos.Pagina(300, 301), () => Datos.Pagina(300, 601));
        var bitacora = new BitacoraFake();
        var servicio = new DescargarLlamadasService(new CuentasFake(Datos.Consu), ccc, guardados, bitacora, new ReporteFake(),
            Options.Create(new DescargaOptions()), new RelojFijo(Hora10.AddHours(5)), NullLogger<DescargarLlamadasService>.Instance);
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(3), default);

        // el guardado del 1er tramo no ha terminado y ya se pidió el 2do a CCC
        await guardados.EsperarLlamadasAsync(1);
        await Esperar(() => ccc.Pedidos.Count >= 2);
        Assert.Empty(bitacora.Registros);   // nada se registra antes de guardarse
        guardados.TerminarTodos();

        var resultado = await turno;

        Assert.Null(resultado.Error);
        Assert.Equal(600, resultado.Llamadas);   // 60 s + 120 s cubren los 3 minutos
        var tramos = bitacora.Registros.Select(r => r.Registro).ToList();
        for (var i = 1; i < tramos.Count; i++)
        {
            Assert.Equal(tramos[i - 1].Hasta.AddSeconds(1), tramos[i].Desde);   // registrados en orden
        }
        Assert.Equal(600, tramos.Sum(t => t.Insertadas));   // cuenta también la página que se guardó en paralelo
        Assert.Equal(Hora10.AddMinutes(3), resultado.DescargadoHasta);
        Assert.Equal(resultado.DescargadoHasta, bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
    }

    [Fact]
    public async Task Si_falla_un_guardado_el_marcador_se_queda_en_ese_tramo_aunque_el_siguiente_ya_se_haya_pedido()
    {
        var guardados = new GuardadoControlado();
        var ccc = new CccFake(() => Datos.Pagina(300, 1), () => Datos.Pagina(300, 301));
        var bitacora = new BitacoraFake();
        var reporte = new ReporteFake();
        var servicio = new DescargarLlamadasService(new CuentasFake(Datos.Consu), ccc, guardados, bitacora, reporte,
            Options.Create(new DescargaOptions()), new RelojFijo(Hora10.AddHours(5)), NullLogger<DescargarLlamadasService>.Instance);
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(3), default);
        await guardados.EsperarLlamadasAsync(1);
        guardados.Fallar(0, new ErrorBaseDatosException("timeout", new Exception()));
        guardados.TerminarTodos();
        var resultado = await turno;

        Assert.NotNull(resultado.Error);
        Assert.Contains("guardar página 1", resultado.Error.Mensaje);
        Assert.Equal(Hora10, resultado.DescargadoHasta);
        var registro = Assert.Single(bitacora.Registros).Registro;   // el 2do tramo no se registra
        Assert.False(registro.Exitosa);
        Assert.Equal(Hora10, bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
        Assert.Equal(1, bitacora.Avance[("2812", TipoDescarga.Llamadas)].ErroresSeguidos);
    }

    private static async Task Esperar(Func<bool> condicion)
    {
        for (var i = 0; i < 200 && !condicion(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condicion());
    }

    [Fact]
    public async Task Un_tramo_recortado_por_el_limite_se_parte_a_la_mitad_de_lo_que_realmente_pidio()
    {
        var ccc = new CccFake(() => Datos.Pagina(1000, 1), () => Datos.Pagina(400, 1));
        var (servicio, _, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10, tamano: 900);

        await servicio.AvanzarAsync(Datos.Consu, Hora10.AddSeconds(40), default);

        Assert.Equal(Hora10.AddSeconds(39), ccc.Pedidos[0].Hasta);   // el límite lo dejó en 40 s
        Assert.Equal(Hora10.AddSeconds(19), ccc.Pedidos[1].Hasta);   // la mitad de 40, no de 900
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
        var o = new DescargaOptions();   // objetivo 900 llamadas, 5 s a 900 s
        Assert.Equal(120, TramosAdaptativos.Siguiente(60, 0, 1, o));          // vacío: el doble
        Assert.Equal(120, TramosAdaptativos.Siguiente(60, 100, 1, o));        // pocas: crece máximo al doble
        Assert.Equal(900, TramosAdaptativos.Siguiente(900, 10, 1, o));        // no pasa del máximo
        Assert.Equal(60, TramosAdaptativos.Siguiente(60, 900, 1, o));         // justo el objetivo: igual
        Assert.Equal(30, TramosAdaptativos.Siguiente(60, 1800, 2, o));        // el doble del objetivo: la mitad
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
    public async Task La_primera_vez_antes_del_umbral_empieza_a_las_00_de_ese_dia()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());

        await servicio.AvanzarAsync(Datos.Consu, new DateTime(2026, 10, 30, 0, 40, 0), default);

        Assert.Equal(new DateTime(2026, 10, 30, 0, 0, 0), bitacora.Registros[0].Registro.Desde);
        Assert.Empty(bitacora.Huecos);
    }

    [Fact]
    public async Task La_primera_vez_ya_avanzado_el_dia_el_vivo_empieza_en_la_hora_actual_y_desde_las_00_queda_por_recuperar()
    {
        var ccc = new CccFake();
        var (servicio, _, bitacora, _) = Crear(ccc);
        var limite = new DateTime(2026, 10, 30, 13, 5, 30);

        var turno = await servicio.AvanzarAsync(Datos.Consu, limite, default);

        Assert.Equal(limite.Date, turno.SaltoDesde);
        Assert.Equal(TimeSpan.Zero, turno.Atraso);
        Assert.Empty(ccc.Pedidos);
        Assert.Equal(limite, bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
        Assert.Equal(new HuecoDescarga(limite.Date, limite, 60), Assert.Single(bitacora.Huecos).Hueco);
    }

    [Fact]
    public async Task Si_estuvo_apagada_dias_el_vivo_salta_a_la_hora_actual_y_nada_se_pierde()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        ConAvance(bitacora, Hora10.AddDays(-3), tamano: 900);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10, default);

        Assert.Equal(Hora10.AddDays(-3), turno.SaltoDesde);
        Assert.Equal(Hora10, bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);
        var hueco = Assert.Single(bitacora.Huecos).Hueco;
        Assert.Equal((Hora10.AddDays(-3), Hora10), (hueco.Desde, hueco.Hasta));   // los 3 días quedan por recuperar
    }

    [Fact]
    public async Task Con_poco_atraso_no_salta()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        ConAvance(bitacora, Hora10, tamano: 60);

        var turno = await servicio.AvanzarAsync(Datos.Consu, Hora10.AddMinutes(59), default);

        Assert.Null(turno.SaltoDesde);
        Assert.Empty(bitacora.Huecos);
        Assert.Equal(Hora10, bitacora.Registros[0].Registro.Desde);
    }

    [Fact]
    public async Task La_recuperacion_baja_el_hueco_mas_viejo_y_lo_borra_al_terminar_sin_tocar_el_vivo()
    {
        var ccc = new CccFake();
        var (servicio, _, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10.AddHours(4), tamano: 60);
        bitacora.Huecos.Add(("2812", new HuecoDescarga(Hora10.AddHours(1), Hora10.AddHours(1).AddMinutes(10), 60)));
        bitacora.Huecos.Add(("2812", new HuecoDescarga(Hora10, Hora10.AddMinutes(10), 60)));

        var r = await servicio.RecuperarAsync(Datos.Consu, default);

        Assert.NotNull(r);
        Assert.Null(r.Turno.Error);
        Assert.Equal(Hora10, ccc.Pedidos[0].Desde);   // el más viejo primero
        Assert.Equal(Hora10.AddMinutes(10).AddSeconds(-1), ccc.Pedidos[^1].Hasta);   // no pasa del fin del hueco
        Assert.All(bitacora.Registros, x => Assert.Equal(Hora10.AddMinutes(10), x.Registro.FinHueco));
        Assert.All(bitacora.Registros, x => Assert.StartsWith(DescargarLlamadasService.EtiquetaRecuperacion, x.Registro.Mensaje));
        Assert.Equal(Hora10.AddHours(1), Assert.Single(bitacora.Huecos).Hueco.Desde);   // el primero se borró
        Assert.Equal(TimeSpan.FromMinutes(10), r.Pendiente);
        Assert.Equal(Hora10.AddHours(4), bitacora.Avance[("2812", TipoDescarga.Llamadas)].DescargadoHasta);   // el vivo no se mueve
    }

    [Fact]
    public async Task Si_falla_un_tramo_de_recuperacion_el_hueco_se_queda_donde_estaba()
    {
        var ccc = new CccFake(() => throw new CccApiException(401, "x"));
        var (servicio, _, bitacora, _) = Crear(ccc);
        ConAvance(bitacora, Hora10.AddHours(4), tamano: 60);
        bitacora.Huecos.Add(("2812", new HuecoDescarga(Hora10, Hora10.AddMinutes(10), 60)));

        var r = await servicio.RecuperarAsync(Datos.Consu, default);

        Assert.NotNull(r!.Turno.Error);
        Assert.Equal(new HuecoDescarga(Hora10, Hora10.AddMinutes(10), 60), Assert.Single(bitacora.Huecos).Hueco);
        Assert.Equal(0, bitacora.Avance[("2812", TipoDescarga.Llamadas)].ErroresSeguidos);   // el error no es del vivo
    }

    [Fact]
    public async Task Sin_huecos_no_hay_recuperacion()
    {
        var (servicio, _, _, _) = Crear(new CccFake());

        Assert.Null(await servicio.RecuperarAsync(Datos.Consu, default));
    }

    private static void ConTramoEnVivo(BitacoraFake bitacora, DateTime desde, DateTime hasta, DateTime fin) =>
        bitacora.Registros.Add((new RegistroTramo("2812", "x", TipoDescarga.Llamadas, desde, hasta, 60, OrigenDescarga.Automatica,
            true, 1, 10, 10, 0, fin, fin, null), true));

    [Fact]
    public async Task El_repaso_pide_solo_lo_que_se_bajo_en_vivo_sin_mover_el_marcador()
    {
        var ccc = new CccFake(() => Datos.Pagina(10));
        var (servicio, llamadas, bitacora, reporte) = Crear(ccc);
        var hoy = Hora10.Date;
        var ayer = hoy.AddDays(-1);
        ConAvance(bitacora, hoy.AddMinutes(45));   // el vivo ya pasó la medianoche
        ConTramoEnVivo(bitacora, ayer.AddHours(9), ayer.AddHours(9).AddMinutes(10).AddSeconds(-1), ayer.AddHours(9).AddMinutes(25));
        ConTramoEnVivo(bitacora, ayer.AddHours(9).AddMinutes(10), ayer.AddHours(9).AddMinutes(20).AddSeconds(-1), ayer.AddHours(9).AddMinutes(35));
        ConTramoEnVivo(bitacora, ayer.AddHours(10), ayer.AddHours(10).AddMinutes(10).AddSeconds(-1), hoy.AddHours(8));   // bajado tarde
        var antes = bitacora.Registros.Count;

        var repaso = await servicio.RepasarDiaAsync(Datos.Consu, DateOnly.FromDateTime(ayer), default);

        Assert.NotNull(repaso);
        Assert.Equal(0, repaso.TramosConError);
        Assert.Equal(ayer.AddHours(9), repaso.Tramos[0].Desde);
        Assert.Equal(ayer.AddHours(9).AddMinutes(20).AddSeconds(-1), repaso.Tramos[^1].Hasta);   // lo bajado tarde no se repasa
        Assert.Equal(10, llamadas.Guardadas.Count);
        var nuevos = bitacora.Registros.Skip(antes).ToList();
        Assert.All(nuevos, r => Assert.False(r.MoverAvance));
        Assert.All(nuevos, r => Assert.StartsWith("Repaso del", r.Registro.Mensaje));
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

    [Fact]
    public async Task El_repaso_espera_si_falta_recuperar_parte_del_dia()
    {
        var (servicio, _, bitacora, _) = Crear(new CccFake());
        var hoy = Hora10.Date;
        ConAvance(bitacora, hoy.AddHours(3));
        bitacora.Huecos.Add(("2812", new HuecoDescarga(hoy.AddHours(-5), hoy.AddHours(2), 60)));

        Assert.Null(await servicio.RepasarDiaAsync(Datos.Consu, DateOnly.FromDateTime(hoy.AddDays(-1)), default));
    }

    [Fact]
    public async Task Si_todo_el_dia_se_bajo_despues_de_pasar_no_hay_repaso()
    {
        var ccc = new CccFake();
        var (servicio, _, bitacora, _) = Crear(ccc);
        var hoy = Hora10.Date;
        ConAvance(bitacora, hoy.AddHours(3));
        ConTramoEnVivo(bitacora, hoy.AddDays(-1).AddHours(9), hoy.AddDays(-1).AddHours(10), hoy.AddHours(2));   // recuperado de madrugada

        var repaso = await servicio.RepasarDiaAsync(Datos.Consu, DateOnly.FromDateTime(hoy.AddDays(-1)), default);

        Assert.NotNull(repaso);   // no hay nada que repasar: se cierran los tiempos de una vez
        Assert.Empty(repaso.Tramos);
        Assert.Empty(ccc.Pedidos);
    }

    [Theory]
    [MemberData(nameof(Fallas))]
    public async Task Un_tramo_con_error_no_mueve_el_avance_y_explica_el_motivo(Exception falla, string motivoEsperado)
    {
        // sin reintentos: si falla el avance no se mueve
        var (servicio, _, bitacora, reporte) = Crear(new CccFake(() => Datos.Pagina(1000), () => throw falla),
            opciones: new DescargaOptions { ReintentosReduciendo = 0 });
        ConAvance(bitacora, Hora10, tamano: 5);   // tramo mínimo: sí pasa a la página 2

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
        var bitacora = new BitacoraFake(falla: true);
        var (servicio, _, _, _) = Crear(new CccFake(() => Datos.Pagina(5)), bitacora);
        ConAvance(bitacora, Hora10);

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
