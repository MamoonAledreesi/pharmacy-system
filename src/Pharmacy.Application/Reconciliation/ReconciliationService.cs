using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reconciliation;

/// <summary>One thing that does not add up: what it is, what the ledger says it should be, and what is stored.</summary>
public sealed record Discrepancy(string Reference, decimal Expected, decimal Actual);

/// <summary>The outcome of one check. <c>Checked</c> is how many records were compared; <c>Discrepancies</c> lists at most <see cref="ReconciliationService.MaxListed"/>.</summary>
public sealed record ReconciliationCheck(string Key, int Checked, int DiscrepancyCount, IReadOnlyList<Discrepancy> Discrepancies)
{
    public bool Passed => DiscrepancyCount == 0;
}

public sealed record ReconciliationReport(DateTime RanAtUtc, IReadOnlyList<ReconciliationCheck> Checks)
{
    public bool Passed => Checks.All(c => c.Passed);
}

/// <summary>
/// Compares every stored balance with the ledger it should equal (stock, cash, customer and supplier accounts) and
/// every invoice total with its lines. A healthy system reports no differences; anything else points to data changed
/// outside the application. Read-only, run on demand, needs Reconciliation.Run.
/// </summary>
public sealed class ReconciliationService(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxListed = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<ReconciliationReport>> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.ReconciliationRun))
        {
            return Result.Failure<ReconciliationReport>(_forbidden);
        }

        IReadOnlyList<ReconciliationCheck> checks =
        [
            await CheckAsync(ReconciliationKeys.BatchAvailable, db.Batches.CountAsync(cancellationToken), BatchAvailable(), cancellationToken),
            await CheckAsync(ReconciliationKeys.BatchQuarantined, db.Batches.CountAsync(cancellationToken), BatchQuarantined(), cancellationToken),
            await CheckAsync(ReconciliationKeys.CashShifts, db.CashShifts.CountAsync(cancellationToken), CashShifts(), cancellationToken),
            await CheckAsync(ReconciliationKeys.CustomerAccounts, db.Customers.CountAsync(cancellationToken), CustomerAccounts(), cancellationToken),
            await CheckAsync(ReconciliationKeys.SupplierAccounts, db.Suppliers.CountAsync(cancellationToken), SupplierAccounts(), cancellationToken),
            await CheckAsync(ReconciliationKeys.SalesInvoices, db.SalesInvoices.CountAsync(cancellationToken), SalesInvoices(), cancellationToken),
            await CheckAsync(ReconciliationKeys.PurchaseInvoices, db.PurchaseInvoices.CountAsync(cancellationToken), PurchaseInvoices(), cancellationToken),
        ];
        return Result.Success(new ReconciliationReport(clock.GetUtcNow().UtcDateTime, checks));
    }

    // Each stored balance is compared with the sum of its own ledger lines, as a correlated subquery per record (index seek).
    private IQueryable<Discrepancy> BatchAvailable() =>
        db.Batches.AsNoTracking()
            .Select(b => new { b.BatchNumber, b.ProductId, Stored = b.QuantityAvailable, Expected = db.StockMovements.Where(m => m.BatchId == b.Id).Sum(m => (int?)m.AvailableDelta) ?? 0 })
            .Where(r => r.Stored != r.Expected)
            .Select(r => new Discrepancy(r.BatchNumber + " #" + r.ProductId, r.Expected, r.Stored));

    private IQueryable<Discrepancy> BatchQuarantined() =>
        db.Batches.AsNoTracking()
            .Select(b => new { b.BatchNumber, b.ProductId, Stored = b.QuantityQuarantined, Expected = db.StockMovements.Where(m => m.BatchId == b.Id).Sum(m => (int?)m.QuarantineDelta) ?? 0 })
            .Where(r => r.Stored != r.Expected)
            .Select(r => new Discrepancy(r.BatchNumber + " #" + r.ProductId, r.Expected, r.Stored));

    // A shift holds its opening balance plus its movements (receipts are positive, payments negative).
    private IQueryable<Discrepancy> CashShifts() =>
        db.CashShifts.AsNoTracking()
            .Select(s => new { s.Id, Stored = s.CurrentBalance, Expected = s.OpeningBalance + (db.CashMovements.Where(m => m.ShiftId == s.Id).Sum(m => (decimal?)m.Amount) ?? 0m) })
            .Where(r => r.Stored != r.Expected)
            .Select(r => new Discrepancy("#" + r.Id, r.Expected, r.Stored));

    private IQueryable<Discrepancy> CustomerAccounts() =>
        db.Customers.AsNoTracking()
            .Select(c => new { c.Name, Stored = c.CurrentBalance, Expected = db.CustomerAccountEntries.Where(e => e.CustomerId == c.Id).Sum(e => (decimal?)e.Amount) ?? 0m })
            .Where(r => r.Stored != r.Expected)
            .Select(r => new Discrepancy(r.Name, r.Expected, r.Stored));

    private IQueryable<Discrepancy> SupplierAccounts() =>
        db.Suppliers.AsNoTracking()
            .Select(s => new { s.Name, Stored = s.CurrentBalance, Expected = db.SupplierAccountEntries.Where(e => e.SupplierId == s.Id).Sum(e => (decimal?)e.Amount) ?? 0m })
            .Where(r => r.Stored != r.Expected)
            .Select(r => new Discrepancy(r.Name, r.Expected, r.Stored));

    // An invoice total equals its lines and equals what was paid in cash plus what was left on credit.
    private IQueryable<Discrepancy> SalesInvoices() =>
        db.SalesInvoices.AsNoTracking()
            .Select(i => new { i.Number, i.Total, Lines = i.Lines.Sum(l => l.NetTotal), Split = i.PaidCash + i.OnCredit })
            .Where(i => i.Total != i.Lines || i.Total != i.Split)
            .Select(i => new Discrepancy(i.Number, i.Lines, i.Total));

    private IQueryable<Discrepancy> PurchaseInvoices() =>
        db.PurchaseInvoices.AsNoTracking()
            .Select(i => new { i.Number, i.Total, Lines = i.Lines.Sum(l => l.LineTotal), Split = i.PaidCash + i.OnCredit })
            .Where(i => i.Total != i.Lines || i.Total != i.Split)
            .Select(i => new Discrepancy(i.Number, i.Lines, i.Total));

    private static async Task<ReconciliationCheck> CheckAsync(string key, Task<int> checkedCount, IQueryable<Discrepancy> mismatches, CancellationToken cancellationToken)
    {
        var total = await checkedCount;
        var count = await mismatches.CountAsync(cancellationToken);
        IReadOnlyList<Discrepancy> listed = count == 0 ? [] : await mismatches.Take(MaxListed).ToListAsync(cancellationToken);
        return new ReconciliationCheck(key, total, count, listed);
    }
}

/// <summary>Resource keys (<c>Reconciliation.Check.*</c>) naming each check.</summary>
public static class ReconciliationKeys
{
    public const string BatchAvailable = "Reconciliation.Check.BatchAvailable";
    public const string BatchQuarantined = "Reconciliation.Check.BatchQuarantined";
    public const string CashShifts = "Reconciliation.Check.CashShifts";
    public const string CustomerAccounts = "Reconciliation.Check.CustomerAccounts";
    public const string SupplierAccounts = "Reconciliation.Check.SupplierAccounts";
    public const string SalesInvoices = "Reconciliation.Check.SalesInvoices";
    public const string PurchaseInvoices = "Reconciliation.Check.PurchaseInvoices";
}
