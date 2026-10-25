using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

/// <summary>Supplier lists, details, and statements. Requires Suppliers.View.</summary>
public sealed class SupplierQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;
    public const int QuickSearchLimit = 20;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<SupplierListItem>>> SearchAsync(SupplierQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.SuppliersView))
        {
            return Result.Failure<PagedResult<SupplierListItem>>(_forbidden);
        }

        var suppliers = Filter(db.Suppliers.AsNoTracking(), query.Text, query.IncludeInactive);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await suppliers.CountAsync(cancellationToken);
        var items = await suppliers
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierListItem(s.Id, s.Name, s.Phone, s.IsActive, s.CurrentBalance))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<SupplierListItem>(items, total));
    }

    /// <summary>Autocomplete for invoices and payments: active suppliers whose name starts with or contains the text.</summary>
    public async Task<Result<IReadOnlyList<SupplierListItem>>> QuickSearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SuppliersView))
        {
            return Result.Failure<IReadOnlyList<SupplierListItem>>(_forbidden);
        }

        var normalized = SearchNormalizer.Normalize(text);
        IReadOnlyList<SupplierListItem> items = await Filter(db.Suppliers.AsNoTracking(), text, includeInactive: false)
            .OrderBy(s => normalized.Length > 0 && s.SearchText.StartsWith(normalized) ? 0 : 1)
            .ThenBy(s => s.Name)
            .Take(QuickSearchLimit)
            .Select(s => new SupplierListItem(s.Id, s.Name, s.Phone, s.IsActive, s.CurrentBalance))
            .ToListAsync(cancellationToken);
        return Result.Success(items);
    }

    public async Task<Result<SupplierDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SuppliersView))
        {
            return Result.Failure<SupplierDetail>(_forbidden);
        }

        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SupplierDetail(
                s.Id, s.Name, s.Phone, s.Address, s.Notes, s.IsActive, s.CurrentBalance,
                db.SupplierAccountEntries.Any(e => e.SupplierId == s.Id && e.Type == SupplierEntryType.OpeningBalance),
                s.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
        return supplier is null ? Result.Failure<SupplierDetail>(new Error(CommonErrors.NotFound)) : Result.Success(supplier);
    }

    /// <summary>The supplier statement, newest entry first, each line with the balance right after it.</summary>
    public async Task<Result<PagedResult<SupplierEntryRow>>> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.SuppliersView))
        {
            return Result.Failure<PagedResult<SupplierEntryRow>>(_forbidden);
        }

        var entries = db.SupplierAccountEntries.AsNoTracking().Where(e => e.SupplierId == query.SupplierId);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(e => e.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(e => new SupplierEntryRow(
                e.Id,
                e.OccurredAtUtc,
                e.Type,
                e.Amount,
                e.BalanceAfter,
                e.Note,
                e.UserName,
                e.ReferenceType,
                e.ReferenceId,
                e.ReferenceType == PurchaseService.InvoiceReference
                    ? db.PurchaseInvoices.Where(i => i.Id == e.ReferenceId).Select(i => i.Number).FirstOrDefault()
                    : null,
                e.Type == SupplierEntryType.Payment
                    && db.SupplierPayments.Any(p => p.Id == e.ReferenceId && p.IsReversed)))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<SupplierEntryRow>(items, total));
    }

    private static IQueryable<Supplier> Filter(IQueryable<Supplier> suppliers, string? text, bool includeInactive)
    {
        if (!includeInactive)
        {
            suppliers = suppliers.Where(s => s.IsActive);
        }

        var normalized = SearchNormalizer.Normalize(text);
        return normalized.Length == 0
            ? suppliers
            : normalized.Split(' ').Aggregate(suppliers, (current, word) => current.Where(s => s.SearchText.Contains(word)));
    }
}
