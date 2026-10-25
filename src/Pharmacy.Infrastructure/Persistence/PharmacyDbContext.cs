using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Auditing;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Settings;
using Pharmacy.Infrastructure.Identity;

namespace Pharmacy.Infrastructure.Persistence;

/// <summary>
/// Single database context for the modular monolith. Each module maps its tables
/// into its own SQL Server schema through IEntityTypeConfiguration classes in this assembly.
/// </summary>
public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, int,
        IdentityUserClaim<int>, IdentityUserRole<int>, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options),
      IPharmacyDbContext
{
    public DbSet<PharmacySettings> PharmacySettings => Set<PharmacySettings>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<DosageForm> DosageForms => Set<DosageForm>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        where TResult : Result =>
        this.InTransactionAsync(work, cancellationToken);

    // UPDLOCK keeps the product's batch rows locked until the transaction ends, so two operations on the same
    // product (for example two sales of the last box) cannot both read the old quantity and both succeed.
    // ROWLOCK keeps the lock to those rows. Locking an empty set locks nothing, which is why creating the very
    // first batch of a product relies on the unique index and the caller retrying on a conflict.
    public Task<List<Batch>> LoadBatchesForUpdateAsync(int productId, CancellationToken cancellationToken = default) =>
        Batches
            .FromSqlInterpolated($"SELECT * FROM inventory.Batches WITH (UPDLOCK, ROWLOCK) WHERE ProductId = {productId}")
            .ToListAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own mapping first; the configurations in this assembly then refine it.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PharmacyDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) by default; unit costs override this to decimal(18,4) explicitly.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }
}
