using System.Globalization;
using System.Text;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Infrastructure.Persistencia;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Reportes;

/// <summary>
/// Sección "Reportes": carpeta de los .txt. Relativa = dentro de la carpeta de la aplicación.
/// </summary>
public sealed class ReportesOptions
{
    public const string Seccion = "Reportes";

    public string Carpeta { get; init; } = "reportes";
}

/// <summary>
/// Una carpeta por cuenta y un .txt por día (reportes\CONSU_2812\CONSU_2812_2026-10-30.txt) con una línea por cada
/// descarga o incidencia, para revisar hora por hora qué pasó. Nunca escribe datos de las llamadas.
/// </summary>
internal sealed class ReporteTxt(
    IOptions<ReportesOptions> options,
    ICuentaCccRepository cuentas,
    TimeProvider timeProvider) : IReporteDescargas
{
    // Dos plataformas escriben en paralelo: un archivo a la vez
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public Task EscribirTramoAsync(RegistroTramo r, CancellationToken cancellationToken)
    {
        var duracion = Duracion(r.Fin - r.Inicio);
        var origen = r.Origen == OrigenDescarga.Automatica ? "automática" : "MANUAL";
        var estado = r.Exitosa ? "OK   " : "ERROR";
        var n = (int v) => v.ToString("N0", Mx);

        var detalle = r.Tipo switch
        {
            TipoDescarga.Llamadas =>
                $"LLAMADAS {r.Desde:dd/MM HH:mm:ss}-{r.Hasta:HH:mm:ss}  |  {n(r.Registros)} llamadas ({n(r.Insertadas)} nuevas, " +
                $"{n(r.Actualizadas)} actualizadas)  |  {r.Paginas} páginas" + (r.Exitosa && r.Mensaje is not null ? $"  |  {r.Mensaje}" : ""),
            TipoDescarga.EstatusAgentes =>
                $"AGENTES  foto {r.Desde:dd/MM HH:mm}  |  {n(r.Registros)} fotos de {n(r.Paginas)} agente(s)",
            _ =>
                $"TIEMPOS  {(r.Hasta > r.Desde ? $"cierre del día {r.Desde:dd/MM}" : $"día en curso {r.Desde:dd/MM HH:mm}")}  |  " +
                $"{n(r.Registros)} agente(s) ({n(r.Insertadas)} nuevos, {n(r.Actualizadas)} actualizados)",
        };

        var linea = $"{estado}    {detalle}  |  {duracion}  |  {origen}" +
                    (r.Exitosa ? "" : $"  |  {r.Mensaje}  |  " +
                        (r.Origen == OrigenDescarga.Manual ? "volver a pedirlo a mano" : "se reintenta en el siguiente turno"));

        return EscribirAsync([r.ClientId], linea, cancellationToken);
    }

    public Task EscribirAvisoAsync(string? clientId, string mensaje, CancellationToken cancellationToken) =>
        EscribirAsync(clientId is null ? cuentas.ObtenerTodas().Select(c => c.ClientId) : [clientId],
            $"AVISO    {mensaje}", cancellationToken);

    private async Task EscribirAsync(IEnumerable<string> clientIds, string texto, CancellationToken cancellationToken)
    {
        var ahora = timeProvider.GetLocalNow().DateTime;
        var raiz = Path.IsPathRooted(options.Value.Carpeta)
            ? options.Value.Carpeta
            : Path.Combine(AppContext.BaseDirectory, options.Value.Carpeta);

        await Candado.WaitAsync(cancellationToken);
        try
        {
            foreach (var clientId in clientIds)
            {
                var nombre = cuentas.ObtenerPorId(clientId)?.Nombre ?? "";
                // Carpeta por cuenta y un archivo por día: CONSU_2812\CONSU_2812_2026-10-30.txt
                var cuenta = TablasCuenta.PorCuenta.TryGetValue(int.Parse(clientId), out var tabla) ? tabla.Sufijo : clientId;
                var carpeta = Path.Combine(raiz, LimpiarNombre(cuenta));
                Directory.CreateDirectory(carpeta);

                var archivo = Path.Combine(carpeta, LimpiarNombre($"{cuenta}_{ahora:yyyy-MM-dd}.txt"));
                var contenido = new StringBuilder();
                if (!File.Exists(archivo))
                {
                    contenido.AppendLine($"Cuenta {clientId} - {nombre}   |   Reporte de descargas del {ahora:dd/MM/yyyy}");
                    contenido.AppendLine(new string('-', 110));
                }

                contenido.AppendLine($"{ahora:HH:mm:ss}  {texto}");
                await File.AppendAllTextAsync(archivo, contenido.ToString(), new UTF8Encoding(true), cancellationToken);
            }
        }
        finally
        {
            Candado.Release();
        }
    }

    private static string Duracion(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";

    private static string LimpiarNombre(string nombre) =>
        string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
