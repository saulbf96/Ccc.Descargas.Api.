using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Application.Llamadas;
using Ccc.Descargas.Domain.Agentes;
using Ccc.Descargas.Domain.Cuentas;
using Ccc.Descargas.Domain.Llamadas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure.Ccc;

internal sealed class CccClient(
    HttpClient httpClient,
    CarrilesCcc carriles,
    IOptions<CccOptions> options,
    TimeProvider timeProvider,
    ILogger<CccClient> logger) : ICccClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // conserva Ñ y acentos
    };

    private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

    public async Task<ResultadoPing> PingAsync(CuentaCcc cuenta, CancellationToken cancellationToken)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            var (statusCode, respuesta) = await EnviarAsync(cuenta, "Ping", new { @string = "Ping" }, cancellationToken);

            logger.LogInformation("Ping a CCC {ClientId}: HTTP {StatusCode} en {DuracionMs} ms",
                cuenta.ClientId, statusCode, cronometro.ElapsedMilliseconds);

            return new ResultadoPing(cuenta.ClientId, cuenta.Nombre, statusCode == 200, statusCode, respuesta, cronometro.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // sin cancelación = timeout
            logger.LogWarning(ex, "Ping a CCC {ClientId} falló por conexión o timeout", cuenta.ClientId);

            return new ResultadoPing(cuenta.ClientId, cuenta.Nombre, false, null, "Error de conexión o timeout", cronometro.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<Llamada>> BuscarLlamadasAsync(
        CuentaCcc cuenta, DateTime desde, DateTime hasta, int pagina, CancellationToken cancellationToken)
    {
        var filtro = new
        {
            datetime_from = desde.ToString(FormatoFecha, CultureInfo.InvariantCulture),
            datetime_to = hasta.ToString(FormatoFecha, CultureInfo.InvariantCulture),
            page = pagina,
        };

        var respuesta = await EnviarOkAsync(cuenta, "CallHistory/Search", filtro, cancellationToken);

        using var documento = JsonDocument.Parse(respuesta);
        var resultados = documento.RootElement.GetProperty("data").GetProperty("results");
        var clientId = int.Parse(cuenta.ClientId, CultureInfo.InvariantCulture);

        var llamadas = new List<Llamada>(resultados.GetArrayLength());
        foreach (var elemento in resultados.EnumerateArray())
        {
            llamadas.Add(CccLlamadaMapper.Mapear(elemento, clientId));
        }

        logger.LogDebug("CallHistory CCC {ClientId} {Desde:yyyy-MM-dd HH:mm:ss}-{Hasta:HH:mm:ss} página {Pagina}: {Llamadas} llamadas",
            cuenta.ClientId, desde, hasta, pagina, llamadas.Count);

        return llamadas;
    }

    public async Task<IReadOnlyList<TiempoAgente>> ObtenerTiemposAgentesAsync(CuentaCcc cuenta, DateOnly dia, CancellationToken cancellationToken)
    {
        var filtro = new
        {
            datetime_from = dia.ToDateTime(TimeOnly.MinValue).ToString(FormatoFecha, CultureInfo.InvariantCulture),
            datetime_to = dia.ToDateTime(new TimeOnly(23, 59, 59)).ToString(FormatoFecha, CultureInfo.InvariantCulture),
            type = "daily",
            filters = Array.Empty<object>(),
        };

        var respuesta = await EnviarOkAsync(cuenta, "Agent/getTimes", filtro, cancellationToken);

        using var documento = JsonDocument.Parse(respuesta);
        var resultados = documento.RootElement.GetProperty("data").GetProperty("results");
        var tiempos = new List<TiempoAgente>(resultados.GetArrayLength());
        foreach (var elemento in resultados.EnumerateArray())
        {
            tiempos.Add(CccAgenteMapper.MapearTiempo(elemento));
        }

        return tiempos;
    }

    public async Task<EstatusAgente?> ObtenerEstatusAgenteAsync(
        CuentaCcc cuenta, long userId, DateTime fechaFoto, CancellationToken cancellationToken)
    {
        // getStatus solo acepta un user_id
        var (statusCode, respuesta) = await EnviarAsync(cuenta, "Agent/getStatus", new { user_id = userId }, cancellationToken);

        // 400 Agent is not online = sin foto
        if (statusCode == 400 && respuesta.Contains("not online", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (statusCode != 200)
        {
            throw new CccApiException(statusCode, $"CCC respondió HTTP {statusCode}: {respuesta[..Math.Min(respuesta.Length, 200)]}",
                ocupado: EstaOcupado(statusCode, respuesta));
        }

        using var documento = JsonDocument.Parse(respuesta);
        return documento.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? CccAgenteMapper.MapearEstatus(data, userId, fechaFoto)
            : null;
    }

    private async Task<string> EnviarOkAsync(CuentaCcc cuenta, string recurso, object data, CancellationToken cancellationToken)
    {
        var (statusCode, respuesta) = await EnviarAsync(cuenta, recurso, data, cancellationToken);
        if (statusCode != 200)
        {
            // solo los primeros caracteres
            throw new CccApiException(statusCode, $"CCC respondió HTTP {statusCode}: {respuesta[..Math.Min(respuesta.Length, 200)]}",
                ocupado: EstaOcupado(statusCode, respuesta));
        }

        return respuesta;
    }

    // si CCC está ocupado (101) espera y reintenta
    private async Task<(int StatusCode, string Respuesta)> EnviarAsync(
        CuentaCcc cuenta, string recurso, object data, CancellationToken cancellationToken)
    {
        var opciones = options.Value;
        for (var intento = 0; ; intento++)
        {
            int statusCode;
            string respuesta;
            using (await carriles.EntrarAsync(cuenta.Plataforma, cancellationToken))
            {
                (statusCode, respuesta) = await EnviarUnaVezAsync(cuenta.ClientId, recurso, data, cancellationToken);
            }

            if (!EstaOcupado(statusCode, respuesta) || intento >= opciones.ReintentosOcupado)
            {
                return (statusCode, respuesta);
            }

            logger.LogInformation("CCC {ClientId} ocupado con otra petición (code 101); reintento {Intento} de {Maximo} en {Segundos} s",
                cuenta.ClientId, intento + 1, opciones.ReintentosOcupado, opciones.EsperaOcupadoSegundos);
            await Task.Delay(TimeSpan.FromSeconds(opciones.EsperaOcupadoSegundos), timeProvider, cancellationToken);
        }
    }

    // code 101 = otra transacción en curso
    private static bool EstaOcupado(int statusCode, string respuesta)
    {
        if (statusCode != 400)
        {
            return false;
        }

        try
        {
            using var documento = JsonDocument.Parse(respuesta);
            return documento.RootElement.TryGetProperty("code", out var codigo)
                && codigo.ValueKind == JsonValueKind.Number
                && codigo.GetInt32() == 101;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<(int StatusCode, string Respuesta)> EnviarUnaVezAsync(
        string clientId, string recurso, object data, CancellationToken cancellationToken)
    {
        if (!options.Value.Cuentas.TryGetValue(clientId, out var cuenta))
        {
            throw new InvalidOperationException($"La cuenta {clientId} no está configurada.");
        }

        var dataJson = JsonSerializer.Serialize(data, JsonOptions);
        var requestDateTime = timeProvider.GetLocalNow().ToString(FormatoFecha, CultureInfo.InvariantCulture);
        var authToken = CccAuthToken.Calcular(clientId, cuenta.Username, cuenta.SecretKey, dataJson, requestDateTime);

        // CCC pide texto plano clave = valor en UTF-8
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
