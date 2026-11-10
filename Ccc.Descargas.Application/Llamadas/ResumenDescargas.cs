using System.Globalization;
using System.Text;
using Ccc.Descargas.Domain.Cuentas;

namespace Ccc.Descargas.Application.Llamadas;

/// <summary>
/// Arma el texto de los correos de inicio y fin del día a partir de la bitácora y del avance de cada cuenta.
/// Sin dependencias (solo datos de entrada) para poder probarlo.
/// Por cuenta: hasta dónde están descargadas las llamadas (y cuánto atraso lleva), cuántas se bajaron en el
/// periodo, tramos con error, fotos de agentes y el último día de tiempos cerrado.
/// </summary>
public static class ResumenDescargas
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public sealed record Correo(string Asunto, string Cuerpo, bool HayProblemas);

    /// <param name="limite">Hasta dónde ya deberían estar las llamadas (ahora - margen).</param>
    /// <param name="atrasoAlerta">Más atraso que esto es un problema.</param>
    public static Correo Armar(
        string titulo, IReadOnlyList<(CuentaCcc Cuenta, ResumenCuenta Resumen)> cuentas,
        DateTime desde, DateTime hasta, DateTime ahora, DateTime limite, TimeSpan atrasoAlerta, string servidor)
    {
        var cuerpo = new StringBuilder();
        var detalle = new StringBuilder();
        var problemas = 0;

        foreach (var (cuenta, resumen) in cuentas)
        {
            var llamadas = resumen.De(TipoDescarga.Llamadas);
            var estatus = resumen.De(TipoDescarga.EstatusAgentes);
            var tiempos = resumen.De(TipoDescarga.TiemposAgentes);
            var errores = (llamadas?.TramosError ?? 0) + (estatus?.TramosError ?? 0) + (tiempos?.TramosError ?? 0);

            string avanceTexto;
            if (resumen.Avance.TryGetValue(TipoDescarga.Llamadas, out var avance))
            {
                var atraso = limite - avance.DescargadoHasta;
                avanceTexto = $"llamadas al {avance.DescargadoHasta:dd/MM HH:mm}" +
                              (atraso > TimeSpan.FromMinutes(1) ? $" (atraso {Duracion(atraso)})" : " (al día)");
                if (atraso > atrasoAlerta)
                {
                    problemas++;
                    detalle.AppendLine($"  - {cuenta.ClientId}: va {Duracion(atraso)} atrasada" +
                                       (avance.ErroresSeguidos > 0 ? $"; {avance.ErroresSeguidos} error(es) seguidos: {avance.UltimoError}" : ""));
                }
            }
            else
            {
                problemas++;
                avanceTexto = "llamadas: todavía sin descargas registradas";
            }

            var cierre = resumen.Avance.TryGetValue(TipoDescarga.TiemposAgentes, out var t)
                ? $"tiempos cerrados hasta el {t.DescargadoHasta.AddDays(-1):dd/MM}"
                : "tiempos: ningún día cerrado";

            cuerpo.AppendLine($"{cuenta.ClientId} {cuenta.Nombre}: {avanceTexto} | {(llamadas?.Registros ?? 0).ToString("N0", Mx)} llamadas " +
                              $"en {(llamadas?.TramosOk ?? 0).ToString("N0", Mx)} tramos | {(estatus?.Registros ?? 0).ToString("N0", Mx)} fotos de agentes | " +
                              $"{cierre} | {errores} con error");

            if (errores > 0)
            {
                problemas++;
                foreach (var r in new[] { llamadas, estatus, tiempos }.Where(r => r is { TramosError: > 0 }))
                {
                    detalle.AppendLine($"  - {cuenta.ClientId} {TiposDescarga.Texto(r!.Tipo)}: {r.TramosError} con error. Último: {r.UltimoError}");
                }
            }
        }

        var hayProblemas = problemas > 0;
        var asunto = $"[{(hayProblemas ? "REVISAR" : "OK")}] {titulo}" + (hayProblemas ? $": {problemas} problema(s)" : "");

        var texto = new StringBuilder();
        texto.AppendLine($"Servidor: {servidor}");
        texto.AppendLine($"Periodo: {desde:dd/MM/yyyy HH:mm} a {hasta:dd/MM/yyyy HH:mm}   |   Generado: {ahora:dd/MM/yyyy HH:mm}");
        texto.AppendLine();
        texto.Append(cuerpo);
        if (detalle.Length > 0)
        {
            texto.AppendLine();
            texto.AppendLine("Problemas (los tramos con error se reintentan solos en el siguiente turno):");
            texto.Append(detalle);
        }
        texto.AppendLine();
        texto.AppendLine("Detalle: carpeta de reportes (un .txt por cuenta y por día) o dbo.TBL_BITACORA_DESCARGAS_CCC.");

        return new Correo(asunto, texto.ToString(), hayProblemas);
    }

    public static string Duracion(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : $"{(int)t.TotalMinutes} min";
}
