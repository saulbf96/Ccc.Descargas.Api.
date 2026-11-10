namespace Ccc.Descargas.Application.Llamadas;

/// <summary>
/// Calcula el tamaño del siguiente tramo de llamadas. Sin dependencias para poder probarlo.
/// CCC tarda más por página mientras más llamadas tenga el rango (2520, una hora: ~37 s por página; un minuto:
/// ~40 veces más rápido), no permite pedir en paralelo (code 101) y la página máxima es de 1,000. Lo más rápido es
/// que cada tramo quepa en UNA página: se apunta a LlamadasObjetivoPorTramo (debajo de 1,000 para no pedir una
/// segunda página vacía) con un tamaño proporcional a lo que trajo el tramo anterior.
/// </summary>
public static class TramosAdaptativos
{
    /// <summary>Cuánto puede achicarse de un tramo al siguiente (rápido: llegó un pico).</summary>
    public const double FactorMinimo = 0.25;

    /// <summary>Cuánto puede crecer de un tramo al siguiente (despacio: el volumen puede volver a subir).</summary>
    public const double FactorMaximo = 2.0;

    /// <summary>El tamaño guardado en el avance (o el inicial), dentro de los límites configurados.</summary>
    public static int Inicial(int? guardado, DescargaOptions o) =>
        Math.Clamp(guardado ?? o.TramoInicialSegundos, Minimo(o), Maximo(o));

    /// <param name="actual">Segundos del tramo que se acaba de bajar.</param>
    /// <param name="llamadas">Llamadas que trajo.</param>
    /// <param name="paginas">Páginas que se pidieron (solo informativo; manda el número de llamadas).</param>
    public static int Siguiente(int actual, int llamadas, int paginas, DescargaOptions o)
    {
        var factor = llamadas <= 0
            ? FactorMaximo
            : Math.Clamp((double)o.LlamadasObjetivoPorTramo / llamadas, FactorMinimo, FactorMaximo);
        var nuevo = (int)Math.Round(actual * factor, MidpointRounding.AwayFromZero);
        return Math.Clamp(nuevo, Minimo(o), Maximo(o));
    }

    /// <summary>Después de un timeout o falla de CCC: el mismo tramo a la mitad (null si ya está en el mínimo).</summary>
    public static int? Reducido(int actual, DescargaOptions o)
    {
        var nuevo = Math.Max(actual / 2, Minimo(o));
        return nuevo < actual ? nuevo : null;
    }

    private static int Minimo(DescargaOptions o) => Math.Min(o.TramoMinimoSegundos, o.TramoMaximoSegundos);

    private static int Maximo(DescargaOptions o) => Math.Max(o.TramoMinimoSegundos, o.TramoMaximoSegundos);
}
