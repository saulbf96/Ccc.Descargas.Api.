namespace Ccc.Descargas.Application.Llamadas;

// calcula el tamaño del siguiente tramo según las llamadas del anterior
public static class TramosAdaptativos
{
    // lo más que se achica
    public const double FactorMinimo = 0.25;

    // lo más que crece
    public const double FactorMaximo = 2.0;

    // tamaño inicial dentro de los límites
    public static int Inicial(int? guardado, DescargaOptions o) =>
        Math.Clamp(guardado ?? o.TramoInicialSegundos, Minimo(o), Maximo(o));

    public static int Siguiente(int actual, int llamadas, int paginas, DescargaOptions o)
    {
        var factor = llamadas <= 0
            ? FactorMaximo
            : Math.Clamp((double)o.LlamadasObjetivoPorTramo / llamadas, FactorMinimo, FactorMaximo);
        var nuevo = (int)Math.Round(actual * factor, MidpointRounding.AwayFromZero);
        return Math.Clamp(nuevo, Minimo(o), Maximo(o));
    }

    // timeout: el mismo tramo a la mitad, null si ya está en el mínimo
    public static int? Reducido(int actual, DescargaOptions o)
    {
        var nuevo = Math.Max(actual / 2, Minimo(o));
        return nuevo < actual ? nuevo : null;
    }

    private static int Minimo(DescargaOptions o) => Math.Min(o.TramoMinimoSegundos, o.TramoMaximoSegundos);

    private static int Maximo(DescargaOptions o) => Math.Max(o.TramoMinimoSegundos, o.TramoMaximoSegundos);
}
