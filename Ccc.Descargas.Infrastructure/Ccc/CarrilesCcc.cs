using System.Collections.Concurrent;

namespace Ccc.Descargas.Infrastructure.Ccc;

/// <summary>
/// Un "carril" por plataforma de CCC (api.ccc.uno = CCC1, app2.ccc.uno = CCC2): cada plataforma atiende UNA
/// petición a la vez, aunque sean de cuentas distintas (comprobado: mientras 2520 descargaba, 2812 recibía
/// code 101). Toda petición (llamadas, agentes, ping, manual o automática) espera su turno aquí, así nunca
/// chocamos entre nosotros. Singleton: compartido por todas las instancias de CccClient.
/// </summary>
internal sealed class CarrilesCcc
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> carriles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> EntrarAsync(string plataforma, CancellationToken cancellationToken)
    {
        var carril = carriles.GetOrAdd(plataforma, _ => new SemaphoreSlim(1, 1));
        await carril.WaitAsync(cancellationToken);
        return new Salida(carril);
    }

    private sealed class Salida(SemaphoreSlim carril) : IDisposable
    {
        private int liberado;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref liberado, 1) == 0)
            {
                carril.Release();
            }
        }
    }
}
