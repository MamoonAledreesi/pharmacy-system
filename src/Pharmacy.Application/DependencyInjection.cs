using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Settings;

namespace Pharmacy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PharmacySettingsState>();
        services.AddScoped<PharmacySettingsService>();

        return services;
    }
}
