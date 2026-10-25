using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pharmacy.Domain.Auditing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;
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

    DbSet<CashShift> CashShifts { get; }

    DbSet<CashMovement> CashMovements { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<SupplierAccountEntry> SupplierAccountEntries { get; }

    DbSet<SupplierPayment> SupplierPayments { get; }

    DbSet<PurchaseInvoice> PurchaseInvoices { get; }

    DbSet<PurchaseReturn> PurchaseReturns { get; }

    DbSet<DocumentSequence> DocumentSequences { get; }

    DbSet<Customer> Customers { get; }

    DbSet<CustomerAccountEntry> CustomerAccountEntries { get; }

    DbSet<DebtCollection> DebtCollections { get; }

    DbSet<SalesInvoice> SalesInvoices { get; }

    DbSet<SalesReturn> SalesReturns { get; }

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

    /// <summary>
    /// Loads the open cash shift (if any) and locks it until the surrounding transaction ends, so concurrent cash
    /// movements are applied one after another and the drawer balance stays exact. Call inside
    /// <see cref="ExecuteInTransactionAsync{TResult}"/>; the shift is tracked for update.
    /// </summary>
    Task<CashShift?> LoadOpenShiftForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a shift by id and locks it until the surrounding transaction ends (tracked for update).</summary>
    Task<CashShift?> LoadShiftForUpdateAsync(int shiftId, CancellationToken cancellationToken = default);

    /// <summary>Loads a supplier and locks it until the surrounding transaction ends, so its balance changes one at a time (tracked for update).</summary>
    Task<Supplier?> LoadSupplierForUpdateAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Loads a customer and locks it until the surrounding transaction ends, so its balance changes one at a time (tracked for update).</summary>
    Task<Customer?> LoadCustomerForUpdateAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a sales invoice until the surrounding transaction ends, so two returns of the same invoice are applied one
    /// after another and cannot both give back the same goods. Returns false when the invoice does not exist.
    /// </summary>
    Task<bool> LockSalesInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Locks a purchase invoice until the surrounding transaction ends, so two returns of it are applied one after another.</summary>
    Task<bool> LockPurchaseInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next number of a document kind for a year, locking its sequence until the surrounding transaction
    /// ends. A sequence that does not exist yet is created, so the very first document of a year relies on the unique
    /// index and the caller retrying on a conflict.
    /// </summary>
    Task<string> NextDocumentNumberAsync(string prefix, int year, CancellationToken cancellationToken = default);
}
