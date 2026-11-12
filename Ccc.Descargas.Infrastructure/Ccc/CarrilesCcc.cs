using System.Collections.Concurrent;

namespace Ccc.Descargas.Infrastructure.Ccc;

// un carril por plataforma: CCC atiende una petición a la vez
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
