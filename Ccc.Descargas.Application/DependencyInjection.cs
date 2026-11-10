using Ccc.Descargas.Application.Agentes;
using Ccc.Descargas.Application.Cuentas;
using Ccc.Descargas.Application.Llamadas;
using Microsoft.Extensions.DependencyInjection;

namespace Ccc.Descargas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PingCuentasService>();
        services.AddScoped<DescargarLlamadasService>();
        services.AddScoped<DescargarAgentesService>();
        return services;
    }
}
