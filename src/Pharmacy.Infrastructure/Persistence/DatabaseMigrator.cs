using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Pharmacy.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Creates the database if needed and applies pending migrations. Intended for development;
    /// production deployments apply migrations as an explicit, reviewed step.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
