using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Application.Receivables;

/// <summary>Customer lists, details, and statements. Requires Customers.View.</summary>
public sealed class CustomerQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;
    public const int QuickSearchLimit = 20;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<CustomerListItem>>> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.CustomersView))
        {
            return Result.Failure<PagedResult<CustomerListItem>>(_forbidden);
        }

        var customers = Filter(db.Customers.AsNoTracking(), query.Text, query.IncludeInactive);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await customers.CountAsync(cancellationToken);
        var items = await customers
            .OrderByDescending(c => c.IsDefaultCash)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItem(c.Id, c.Name, c.Phone, c.IsActive, c.IsDefaultCash, c.CurrentBalance))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<CustomerListItem>(items, total));
    }

    /// <summary>Autocomplete for invoices and collections: active customers whose name starts with or contains the text.</summary>
    public async Task<Result<IReadOnlyList<CustomerListItem>>> QuickSearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CustomersView))
        {
            return Result.Failure<IReadOnlyList<CustomerListItem>>(_forbidden);
        }

        var normalized = SearchNormalizer.Normalize(text);
        IReadOnlyList<CustomerListItem> items = await Filter(db.Customers.AsNoTracking(), text, includeInactive: false)
            .OrderBy(c => normalized.Length > 0 && c.SearchText.StartsWith(normalized) ? 0 : 1)
            .ThenBy(c => c.Name)
            .Take(QuickSearchLimit)
            .Select(c => new CustomerListItem(c.Id, c.Name, c.Phone, c.IsActive, c.IsDefaultCash, c.CurrentBalance))
            .ToListAsync(cancellationToken);
        return Result.Success(items);
    }

    /// <summary>The name of another customer using the same phone number, so a page can warn about a likely duplicate.</summary>
    public async Task<Result<string?>> FindPhoneOwnerAsync(string? phone, int? exceptId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CustomersView))
        {
            return Result.Failure<string?>(_forbidden);
        }

        var clean = phone?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return Result.Success<string?>(null);
        }

        var owner = await db.Customers.AsNoTracking()
            .Where(c => c.Phone == clean && c.Id != exceptId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return Result.Success<string?>(owner);
    }

    public async Task<Result<CustomerDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CustomersView))
        {
            return Result.Failure<CustomerDetail>(_forbidden);
        }

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetail(
                c.Id, c.Name, c.Phone, c.Address, c.Notes, c.IsActive, c.IsDefaultCash, c.CurrentBalance,
                db.CustomerAccountEntries.Any(e => e.CustomerId == c.Id && e.Type == CustomerEntryType.OpeningBalance),
                c.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
        return customer is null ? Result.Failure<CustomerDetail>(new Error(CommonErrors.NotFound)) : Result.Success(customer);
    }

    /// <summary>The customer statement, newest entry first, each line with the balance right after it.</summary>
    public async Task<Result<PagedResult<CustomerEntryRow>>> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.CustomersView))
        {
            return Result.Failure<PagedResult<CustomerEntryRow>>(_forbidden);
        }

        var entries = db.CustomerAccountEntries.AsNoTracking().Where(e => e.CustomerId == query.CustomerId);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(e => e.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(e => new CustomerEntryRow(
                e.Id,
                e.OccurredAtUtc,
                e.Type,
                e.Amount,
                e.BalanceAfter,
                e.Note,
                e.UserName,
                e.ReferenceType,
                e.ReferenceId,
                e.ReferenceType == ReceivablesService.CollectionReference
                    ? db.DebtCollections.Where(c => c.Id == e.ReferenceId).Select(c => c.Number).FirstOrDefault()
                    : null,
                e.Type == CustomerEntryType.Collection
                    && db.DebtCollections.Any(c => c.Id == e.ReferenceId && c.IsReversed)))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<CustomerEntryRow>(items, total));
    }

    private static IQueryable<Customer> Filter(IQueryable<Customer> customers, string? text, bool includeInactive)
    {
        if (!includeInactive)
        {
            customers = customers.Where(c => c.IsActive);
        }

        var normalized = SearchNormalizer.Normalize(text);
        return normalized.Length == 0
            ? customers
            : normalized.Split(' ').Aggregate(customers, (current, word) => current.Where(c => c.SearchText.Contains(word)));
    }
}
