using Ccc.Descargas.Application.Abstractions;
using Ccc.Descargas.Infrastructure.Ccc;
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
                "Cada cuenta de Ccc:Cuentas debe tener SecretKey (configúrala en User Secrets).")
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<CccOptions>, DescifrarSecretKeys>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICuentaCccRepository, CuentaCccRepository>();

        services.AddHttpClient<ICccClient, CccClient>((provider, client) =>
        {
            var opciones = provider.GetRequiredService<IOptions<CccOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSegundos);
        });

        return services;
    }
}