using Ccc.Descargas.Application.Cuentas;
using Microsoft.Extensions.DependencyInjection;

namespace Ccc.Descargas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PingCuentasService>();
        return services;



    }
}