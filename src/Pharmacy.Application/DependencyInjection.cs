using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Settings;

namespace Pharmacy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<AuditLogQueryService>();

        services.AddSingleton<PharmacySettingsState>();
        services.AddScoped<PharmacySettingsService>();

        return services;
    }
}
