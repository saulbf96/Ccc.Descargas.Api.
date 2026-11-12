using System.ComponentModel.DataAnnotations;

namespace Ccc.Descargas.Application.Llamadas;

// sección DescargaAutomatica
public sealed class DescargaOptions
{
    public const string Seccion = "DescargaAutomatica";

    public bool Habilitada { get; init; } = true;

    // baja hasta ahora - MargenMinutos
    [Range(0, 180)]
    public int MargenMinutos { get; init; } = 15;

    // tramos adaptativos (ver TramosAdaptativos)
    [Range(1, 3600)]
    public int TramoInicialSegundos { get; init; } = 60;

    [Range(1, 3600)]
    public int TramoMinimoSegundos { get; init; } = 5;

    [Range(1, 3600)]
    public int TramoMaximoSegundos { get; init; } = 900;

    // llamadas por tramo, menos de 1000 para que quepa en una página
    [Range(100, 1000)]
    public int LlamadasObjetivoPorTramo { get; init; } = 800;

    // reintentos con el tramo a la mitad
    [Range(0, 6)]
    public int ReintentosReduciendo { get; init; } = 3;

    // tiempo máximo por turno
    [Range(1, 60)]
    public int MinutosPorTurno { get; init; } = 1;

    // cada cuánto se toma la foto de agentes
    [Range(1, 60)]
    public int MinutosEntreFotosAgentes { get; init; } = 5;

    // hora para cerrar el día anterior
    public TimeSpan HoraCierreDia { get; init; } = new(1, 0, 0);

    // repaso de las llamadas de ayer
    public bool RepasarDiaAnterior { get; init; } = true;

    // días máximos para cerrar hacia atrás
    [Range(1, 31)]
    public int DiasCierreAtras { get; init; } = 7;

    // atraso para mandar correo
    [Range(5, 1440)]
    public int MinutosAtrasoAlerta { get; init; } = 60;

    // espera cuando está al día
    [Range(5, 600)]
    public int SegundosEsperaSinPendientes { get; init; } = 60;
}
