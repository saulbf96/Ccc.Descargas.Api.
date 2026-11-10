using System.ComponentModel.DataAnnotations;

namespace Ccc.Descargas.Application.Llamadas;

/// <summary>
/// Sección "DescargaAutomatica". Cada plataforma de CCC (CCC1, CCC2) atiende UNA petición a la vez, así que
/// sus cuentas se atienden por turnos: en cada turno una cuenta toma la foto de sus agentes (si ya toca) y
/// avanza sus llamadas por tramos hasta MinutosPorTurno; luego sigue la siguiente cuenta.
/// </summary>
public sealed class DescargaOptions
{
    public const string Seccion = "DescargaAutomatica";

    public bool Habilitada { get; init; } = true;

    /// <summary>Las llamadas se bajan hasta "ahora - MargenMinutos", para que CCC termine de cerrarlas.</summary>
    [Range(0, 180)]
    public int MargenMinutos { get; init; } = 15;

    /// <summary>
    /// Tramos adaptativos: pedir rangos chicos a CCC es mucho más rápido (medido: 1 min ~40x más rápido
    /// que la hora completa). Cada tramo apunta a LlamadasObjetivoPorTramo (una sola página); el siguiente
    /// es proporcional a lo que trajo el anterior (ver TramosAdaptativos).
    /// </summary>
    [Range(1, 3600)]
    public int TramoInicialSegundos { get; init; } = 60;

    [Range(1, 3600)]
    public int TramoMinimoSegundos { get; init; } = 5;

    [Range(1, 3600)]
    public int TramoMaximoSegundos { get; init; } = 900;

    /// <summary>Llamadas a las que apunta cada tramo: debajo de las 1,000 de una página para que quepa en una sola.</summary>
    [Range(100, 1000)]
    public int LlamadasObjetivoPorTramo { get; init; } = 800;

    /// <summary>Si CCC tarda demasiado o falla (5xx), el mismo tramo se reintenta a la mitad hasta estas veces.</summary>
    [Range(0, 6)]
    public int ReintentosReduciendo { get; init; } = 3;

    /// <summary>Tiempo máximo que una cuenta usa la plataforma por turno antes de cederla a la siguiente.</summary>
    [Range(1, 60)]
    public int MinutosPorTurno { get; init; } = 1;

    /// <summary>Cada cuánto se toma la foto de estatus de los agentes (y se actualizan sus tiempos del día).</summary>
    [Range(1, 60)]
    public int MinutosEntreFotosAgentes { get; init; } = 5;

    /// <summary>
    /// A partir de esta hora se cierra el día anterior: primero el repaso de sus llamadas (si RepasarDiaAnterior)
    /// y luego sus tiempos de agentes. Una hora después de medianoche para que CCC termine de cerrar todo.
    /// </summary>
    public TimeSpan HoraCierreDia { get; init; } = new(1, 0, 0);

    /// <summary>Al cerrar AYER, volver a pedir sus llamadas completas para recoger lo que haya faltado.</summary>
    public bool RepasarDiaAnterior { get; init; } = true;

    /// <summary>Si los días sin cerrar son más, solo se cierran los últimos (lo anterior se pide a mano).</summary>
    [Range(1, 31)]
    public int DiasCierreAtras { get; init; } = 7;

    /// <summary>Si una cuenta va más atrasada que esto, se avisa por correo (máximo uno por hora).</summary>
    [Range(5, 1440)]
    public int MinutosAtrasoAlerta { get; init; } = 60;

    /// <summary>Cuando todas las cuentas de la plataforma están al día, se espera esto antes de volver a revisar.</summary>
    [Range(5, 600)]
    public int SegundosEsperaSinPendientes { get; init; } = 60;
}
