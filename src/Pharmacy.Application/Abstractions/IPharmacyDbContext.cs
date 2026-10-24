using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pharmacy.Domain.Auditing;
using Pharmacy.Domain.Catalog;
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

    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
