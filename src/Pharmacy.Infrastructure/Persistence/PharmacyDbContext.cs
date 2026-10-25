using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Auditing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;
using Pharmacy.Domain.Receivables;
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

    public DbSet<CashShift> CashShifts => Set<CashShift>();

    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<SupplierAccountEntry> SupplierAccountEntries => Set<SupplierAccountEntry>();

    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();

    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerAccountEntry> CustomerAccountEntries => Set<CustomerAccountEntry>();

    public DbSet<DebtCollection> DebtCollections => Set<DebtCollection>();

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

    // UPDLOCK keeps the shift row locked until the transaction ends, so two cash movements cannot both read the
    // old drawer balance and both be applied to it. At most one shift is open (a unique index guarantees it).
    public async Task<CashShift?> LoadOpenShiftForUpdateAsync(CancellationToken cancellationToken = default) =>
        (await CashShifts
            .FromSqlInterpolated($"SELECT * FROM cash.CashShifts WITH (UPDLOCK, ROWLOCK) WHERE IsOpen = 1")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    public async Task<CashShift?> LoadShiftForUpdateAsync(int shiftId, CancellationToken cancellationToken = default) =>
        (await CashShifts
            .FromSqlInterpolated($"SELECT * FROM cash.CashShifts WITH (UPDLOCK, ROWLOCK) WHERE Id = {shiftId}")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    // UPDLOCK keeps the supplier row locked until the transaction ends, so two payments or invoices cannot both read
    // the old balance and both be applied to it.
    public async Task<Supplier?> LoadSupplierForUpdateAsync(int supplierId, CancellationToken cancellationToken = default) =>
        (await Suppliers
            .FromSqlInterpolated($"SELECT * FROM purchasing.Suppliers WITH (UPDLOCK, ROWLOCK) WHERE Id = {supplierId}")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    // UPDLOCK keeps the customer row locked until the transaction ends, so two collections or sales cannot both read
    // the old balance and both be applied to it.
    public async Task<Customer?> LoadCustomerForUpdateAsync(int customerId, CancellationToken cancellationToken = default) =>
        (await Customers
            .FromSqlInterpolated($"SELECT * FROM receivables.Customers WITH (UPDLOCK, ROWLOCK) WHERE Id = {customerId}")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    // The sequence row is locked until the transaction ends, so two invoices never get the same number, and a
    // rolled-back invoice gives its number back (numbers have no gaps).
    public async Task<string> NextDocumentNumberAsync(string prefix, int year, CancellationToken cancellationToken = default)
    {
        var sequence = (await DocumentSequences
                .FromSqlInterpolated($"SELECT * FROM config.DocumentSequences WITH (UPDLOCK, ROWLOCK) WHERE Prefix = {prefix} AND Year = {year}")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (sequence is null)
        {
            sequence = new DocumentSequence(prefix, year);
            DocumentSequences.Add(sequence);
        }

        return sequence.Next();
    }

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
