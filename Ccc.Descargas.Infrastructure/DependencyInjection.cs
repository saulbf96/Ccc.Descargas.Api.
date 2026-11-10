using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Infrastructure.Ccc;
using Ccc.Descargas.Infrastructure.Notificaciones;
using Ccc.Descargas.Infrastructure.Persistencia;
using Ccc.Descargas.Infrastructure.Reportes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ccc.Descargas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Valida la configuración al arrancar: si falta una SecretKey, la app no inicia
        services.AddOptions<CccOptions>()
            .Bind(configuration.GetSection(CccOptions.Seccion))
            .ValidateDataAnnotations()
            .Validate(o => o.Cuentas.Values.All(c => !string.IsNullOrWhiteSpace(c.SecretKey)),
                "Cada cuenta de Ccc:Cuentas debe tener SecretKey (configúra en User Secrets).")
            .Validate(o => o.Cuentas.Keys.All(id => int.TryParse(id, out var clientId) && TablasCuenta.PorCuenta.ContainsKey(clientId)),
                "Cada cuenta de Ccc:Cuentas debe tener sus tablas y su SP dbo.USP_CCC_<cuenta> (scripts y TablasCuenta.cs).")
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<CccOptions>, DescifrarSecretKeys>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICuentaCccRepository, CuentaCccRepository>();

        // Un carril por plataforma de CCC, compartido por todas las peticiones (CCC atiende una a la vez)
        services.AddSingleton<CarrilesCcc>();

        services.AddHttpClient<ICccClient, CccClient>((provider, client) =>
        {
            var opciones = provider.GetRequiredService<IOptions<CccOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSegundos);
        })
        // gzip/deflate/brotli: pide la respuesta comprimida (una página de 1,000 llamadas son varios MB de JSON).
        // Si CCC no comprime, no pasa nada: llega igual que antes.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),   // renueva conexiones (cambios de DNS de CCC)
        });

        // Si falta la cadena de conexión, la app no inicia
        services.AddOptions<BaseDatosOptions>()
            .Bind(configuration.GetSection(BaseDatosOptions.Seccion))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<BaseDatosOptions>, DescifrarCadenaConexion>();

        services.AddScoped<ILlamadaRepository, LlamadaRepository>();
        services.AddScoped<IAgenteRepository, AgenteRepository>();
        services.AddScoped<IBitacoraDescargas, BitacoraDescargasRepository>();

        // Reporte .txt por cuenta y por día
        services.AddOptions<ReportesOptions>().Bind(configuration.GetSection(ReportesOptions.Seccion));
        services.AddSingleton<IReporteDescargas, ReporteTxt>();

        // Avisos por correo; si está habilitado y le falta algo, la app no inicia
        services.AddOptions<CorreoOptions>()
            .Bind(configuration.GetSection(CorreoOptions.Seccion))
            .Validate(o => !o.Habilitado || (!string.IsNullOrWhiteSpace(o.Servidor) && !string.IsNullOrWhiteSpace(o.De) && o.Para.Length > 0),
                "Correo:Habilitado = true necesita Correo:Servidor, Correo:De y al menos un Correo:Para.")
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<CorreoOptions>, DescifrarContrasenaCorreo>();
        services.AddSingleton<INotificador, NotificadorCorreo>();

        return services;
    }
}

