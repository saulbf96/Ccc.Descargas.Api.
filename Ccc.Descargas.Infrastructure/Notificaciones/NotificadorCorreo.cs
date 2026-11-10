using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Infrastructure.Seguridad;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ccc.Descargas.Infrastructure.Notificaciones;

/// <summary>
/// Sección "Correo". Mientras Habilitado = false no se manda nada (solo queda en el log).
/// La Contrasena NUNCA va en appsettings: en el servidor va como variable cifrada con DPAPI (Correo__Contrasena).
/// </summary>
public sealed class CorreoOptions
{
    public const string Seccion = "Correo";

    public bool Habilitado { get; init; }

    public string Servidor { get; init; } = string.Empty;

    public int Puerto { get; init; } = 587;

    /// <summary>StartTls (587, Office 365 / Gmail), SslOnConnect (465) o Ninguna (relay interno, 25).</summary>
    public string Seguridad { get; init; } = "StartTls";

    public string Usuario { get; init; } = string.Empty;

    // set (no init) para descifrarla una sola vez al arrancar
    public string Contrasena { get; set; } = string.Empty;

    public string De { get; init; } = string.Empty;

    public string[] Para { get; init; } = [];
}

/// <summary>Descifra la contraseña del correo una sola vez, al cargar la configuración.</summary>
internal sealed class DescifrarContrasenaCorreo : IPostConfigureOptions<CorreoOptions>
{
    public void PostConfigure(string? name, CorreoOptions options) =>
        options.Contrasena = ProteccionDpapi.DescifrarSiAplica(options.Contrasena, "la contraseña del correo (Correo:Contrasena)");
}

/// <summary>
/// Manda los avisos por SMTP. Si el correo falla, NO tumba la descarga: solo queda en el log.
/// </summary>
internal sealed class NotificadorCorreo(IOptions<CorreoOptions> options, ILogger<NotificadorCorreo> logger) : INotificador
{
    public async Task EnviarAsync(string asunto, string cuerpo, CancellationToken cancellationToken)
    {
        if (!options.Value.Habilitado)
        {
            logger.LogInformation("Aviso (correo deshabilitado): {Asunto}", asunto);
            return;
        }

        try
        {
            await EnviarOFallarAsync(asunto, cuerpo, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "No se pudo mandar el correo de aviso: {Asunto}", asunto);
        }
    }

    public async Task<string?> ProbarAsync(CancellationToken cancellationToken)
    {
        var correo = options.Value;
        if (!correo.Habilitado)
        {
            return "El correo está deshabilitado (Correo:Habilitado = false).";
        }

        try
        {
            await EnviarOFallarAsync("Correo de prueba",
                $"Si lees esto, los avisos de la descarga CCC funcionan.\nServidor: {Environment.MachineName}\n" +
                $"Enviado: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\nSMTP: {correo.Servidor}:{correo.Puerto} ({correo.Seguridad})",
                cancellationToken);
            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // El motivo exacto (servidor, puerto, autenticación...) para corregir la configuración; nunca la contraseña
            logger.LogError(ex, "Falló el correo de prueba");
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task EnviarOFallarAsync(string asunto, string cuerpo, CancellationToken cancellationToken)
    {
        var correo = options.Value;
        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(correo.De));
        foreach (var para in correo.Para)
        {
            mensaje.To.Add(MailboxAddress.Parse(para));
        }
        mensaje.Subject = $"[CCC Descargas] {asunto}";
        mensaje.Body = new TextPart("plain") { Text = cuerpo };

        using var smtp = new SmtpClient { Timeout = 30_000 };
        var seguridad = correo.Seguridad switch
        {
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "Ninguna" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        await smtp.ConnectAsync(correo.Servidor, correo.Puerto, seguridad, cancellationToken);
        if (!string.IsNullOrEmpty(correo.Usuario))
        {
            await smtp.AuthenticateAsync(correo.Usuario, correo.Contrasena, cancellationToken);
        }
        await smtp.SendAsync(mensaje, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }
}
