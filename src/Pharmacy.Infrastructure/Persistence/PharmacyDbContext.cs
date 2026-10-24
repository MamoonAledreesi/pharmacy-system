using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Infrastructure.Persistence;

/// <summary>
/// Single database context for the modular monolith. Each module maps its tables
/// into its own SQL Server schema through IEntityTypeConfiguration classes in this assembly.
/// </summary>
public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
    : DbContext(options), IPharmacyDbContext
{
    public DbSet<PharmacySettings> PharmacySettings => Set<PharmacySettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PharmacyDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) by default; unit costs override this to decimal(18,4) explicitly.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }
}
