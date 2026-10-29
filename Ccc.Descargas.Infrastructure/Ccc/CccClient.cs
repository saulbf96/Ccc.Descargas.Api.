using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Domain.Cuentas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

internal sealed class CccClient(
    HttpClient httpClient,
    IOptions<CccOptions> options,
    TimeProvider timeProvider,
    ILogger<CccClient> logger) : ICccClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // conserva Ñ y acentos tal cual
    };

    public async Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            var (statusCode, respuesta) = await EnviarAsync(cuenta.ClientId, "Ping", new { @string = "Ping" }, cancellationToken);

            logger.LogInformation("Ping a CCC {ClientId}: HTTP {StatusCode} en {DuracionMs} ms",
                cuenta.ClientId, statusCode, cronometro.ElapsedMilliseconds);

            return new ResultadoPing(cuenta.ClientId, cuenta.Nombre, statusCode == 200, statusCode, respuesta, cronometro.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // TaskCanceledException sin que nadie haya cancelado = se agotó el timeout
            logger.LogWarning(ex, "Ping a CCC {ClientId} falló por conexión o timeout", cuenta.ClientId);

            return new ResultadoPing(cuenta.ClientId, cuenta.Nombre, false, null, "Error de conexión o timeout", cronometro.ElapsedMilliseconds);
        }
    }

    // Genérico para reutilizarlo después con CallHistory/Search y los demás recursos
    private async Task<(int StatusCode, string Respuesta)> EnviarAsync(
        string clientId, string recurso, object data, CancellationToken cancellationToken)
    {
        if (!options.Value.Cuentas.TryGetValue(clientId, out var cuenta))
        {
            throw new InvalidOperationException($"La cuenta {clientId} no está configurada.");
        }

        var dataJson = JsonSerializer.Serialize(data, JsonOptions);
        var requestDateTime = timeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var authToken = CccAuthToken.Calcular(clientId, cuenta.Username, cuenta.SecretKey, dataJson, requestDateTime);

        // CCC espera texto plano con una línea "clave = valor" por parámetro, en UTF-8.
        // Verificado el 2026-10-28: form-urlencoded responde 401 y latin-1 responde 500.
        var body =
            $"AuthUsername = {cuenta.Username}\r\n" +
            $"AuthToken = {authToken}\r\n" +
            $"RequestDateTime = {requestDateTime}\r\n" +
            $"Data = {dataJson}";

        var url = new Uri(new Uri(cuenta.BaseUrl), $"/ws/{recurso}");
        using var contenido = new StringContent(body, Encoding.UTF8, "text/plain");
        using var respuesta = await httpClient.PostAsync(url, contenido, cancellationToken);

        return ((int)respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(cancellationToken));


    }
}