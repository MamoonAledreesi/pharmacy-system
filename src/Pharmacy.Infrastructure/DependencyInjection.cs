using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Abstractions;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PharmacyDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        // No EnableRetryOnFailure: financial use cases open explicit transactions, which a
        // retrying execution strategy rejects. The app runs on a LAN next to its database.
        services.AddDbContext<PharmacyDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IPharmacyDbContext>(provider => provider.GetRequiredService<PharmacyDbContext>());

        return services;
    }
}
