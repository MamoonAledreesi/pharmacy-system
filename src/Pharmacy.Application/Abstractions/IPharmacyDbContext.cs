using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pharmacy.Domain.Auditing;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Application.Abstractions;

/// <summary>
/// The slice of the database context that application services use. Keeps Application free of
/// the concrete SQL Server context while still using EF Core directly (no generic repositories).
/// </summary>
public interface IPharmacyDbContext
{
    DbSet<PharmacySettings> PharmacySettings { get; }

    DbSet<AuditLogEntry> AuditLogEntries { get; }

    DbSet<Product> Products { get; }

    DbSet<Category> Categories { get; }

    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }

    DbSet<DosageForm> DosageForms { get; }

    DbSet<Batch> Batches { get; }

    DbSet<StockMovement> StockMovements { get; }

    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the work in one database transaction: pending changes are saved and committed when it succeeds,
    /// and everything is rolled back when it returns a failed result or throws.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        where TResult : Result;

    /// <summary>
    /// Loads all batches of a product and locks them until the surrounding transaction ends, so concurrent
    /// operations on the same product's stock run one after another. Must be called inside
    /// <see cref="ExecuteInTransactionAsync{TResult}"/>; the returned batches are tracked for update.
    /// </summary>
    Task<List<Batch>> LoadBatchesForUpdateAsync(int productId, CancellationToken cancellationToken = default);
}
