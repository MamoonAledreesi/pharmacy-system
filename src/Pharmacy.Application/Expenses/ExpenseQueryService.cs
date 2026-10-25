using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.Application.Expenses;

/// <summary>The list of expenses and what was spent in a period. Requires Expenses.View.</summary>
public sealed class ExpenseQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<ExpenseListItem>>> SearchAsync(ExpenseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.ExpensesView))
        {
            return Result.Failure<PagedResult<ExpenseListItem>>(_forbidden);
        }

        var expenses = Filter(query);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await expenses.CountAsync(cancellationToken);
        var items = await (
                from e in expenses.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.Id)
                    .Skip(Math.Max(query.PageIndex, 0) * pageSize).Take(pageSize)
                join c in db.ExpenseCategories on e.CategoryId equals c.Id
                orderby e.ExpenseDate descending, e.Id descending
                select new ExpenseListItem(
                    e.Id, e.Number, e.ExpenseDate, new LocalizedName(c.NameArabic, c.NameEnglish), e.Amount, e.Description,
                    e.Payee, e.UserName, e.IsReversed, e.ReverseReason, e.ReversedByUserName))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<ExpenseListItem>(items, total));
    }

    /// <summary>What was spent for the same filters as <see cref="SearchAsync"/>, in total and per category. Reversed expenses are left out.</summary>
    public async Task<Result<ExpenseTotals>> GetTotalsAsync(ExpenseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.ExpensesView))
        {
            return Result.Failure<ExpenseTotals>(_forbidden);
        }

        var counted = Filter(query with { IncludeReversed = false });
        var byCategory = await (
                from e in counted
                join c in db.ExpenseCategories on e.CategoryId equals c.Id
                group e by new { c.Id, c.NameArabic, c.NameEnglish } into g
                orderby g.Sum(x => x.Amount) descending
                select new ExpenseCategoryTotal(g.Key.Id, new LocalizedName(g.Key.NameArabic, g.Key.NameEnglish), g.Sum(x => x.Amount), g.Count()))
            .ToListAsync(cancellationToken);

        return Result.Success(new ExpenseTotals(byCategory.Sum(c => c.Total), byCategory.Sum(c => c.Count), byCategory));
    }

    private IQueryable<Expense> Filter(ExpenseQuery query)
    {
        var expenses = db.Expenses.AsNoTracking();
        if (!query.IncludeReversed)
        {
            expenses = expenses.Where(e => !e.IsReversed);
        }

        if (query.CategoryId is { } categoryId)
        {
            expenses = expenses.Where(e => e.CategoryId == categoryId);
        }

        if (query.From is { } from)
        {
            expenses = expenses.Where(e => e.ExpenseDate >= from);
        }

        if (query.To is { } to)
        {
            expenses = expenses.Where(e => e.ExpenseDate <= to);
        }

        var text = query.Text?.Trim();
        return string.IsNullOrEmpty(text)
            ? expenses
            : expenses.Where(e => e.Number.Contains(text) || e.Description.Contains(text) || (e.Payee != null && e.Payee.Contains(text)));
    }
}
