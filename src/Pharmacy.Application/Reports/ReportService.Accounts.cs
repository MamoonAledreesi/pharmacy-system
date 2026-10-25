using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reports;

public sealed partial class ReportService
{
    private async Task<Result<ReportTable>> CustomerBalancesAsync(CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking()
            .Where(c => c.CurrentBalance != 0)
            .OrderByDescending(c => c.CurrentBalance).ThenBy(c => c.Name)
            .Select(c => new { c.Name, c.Phone, c.CurrentBalance })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = customers
            .Select(c => (IReadOnlyList<object?>)new object?[] { c.Name, c.Phone, c.CurrentBalance })
            .ToList();

        return Done(ReportKey.CustomerBalances, [], [Col("Customer"), Col("Phone"), Col("Balance", ReportColumnKind.Money)], rows, 2);
    }

    private async Task<Result<ReportTable>> SupplierBalancesAsync(CancellationToken cancellationToken)
    {
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => s.CurrentBalance != 0)
            .OrderByDescending(s => s.CurrentBalance).ThenBy(s => s.Name)
            .Select(s => new { s.Name, s.Phone, s.CurrentBalance })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = suppliers
            .Select(s => (IReadOnlyList<object?>)new object?[] { s.Name, s.Phone, s.CurrentBalance })
            .ToList();

        return Done(ReportKey.SupplierBalances, [], [Col("Supplier"), Col("Phone"), Col("Balance", ReportColumnKind.Money)], rows, 2);
    }

    // Reversed collections are listed but do not count in the total.
    private async Task<Result<ReportTable>> CollectionsAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var collections = await (
                from d in db.DebtCollections.AsNoTracking().Where(d => d.CollectedAtUtc >= period.FromUtc && d.CollectedAtUtc < period.ToUtc)
                join c in db.Customers on d.CustomerId equals c.Id
                orderby d.CollectedAtUtc, d.Id
                select new { d.Number, d.CollectedAtUtc, Customer = c.Name, d.Amount, d.UserName, d.Note, d.IsReversed })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = collections
            .Select(d => (IReadOnlyList<object?>)new object?[]
            {
                d.Number, Local(d.CollectedAtUtc), d.Customer, d.Amount, d.UserName, d.Note,
                new ReportText(d.IsReversed ? "Reports.Status.Reversed" : "Reports.Status.Active"),
            })
            .ToList();

        var result = Done(ReportKey.Collections, PeriodFilters(period),
            [Col("Number"), Col("DateTime", ReportColumnKind.DateTime), Col("Customer"), Col("Amount", ReportColumnKind.Money),
             Col("User"), Col("Note"), Col("Status")],
            rows);
        return result.IsFailure
            ? result
            : Result.Success(result.Value with
            {
                Totals = [new ReportText("Reports.TotalRow"), null, null, collections.Where(d => !d.IsReversed).Sum(d => d.Amount), null, null, null],
            });
    }

    private async Task<Result<ReportTable>> CustomerStatementAsync(ReportPeriod period, int? customerId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id)
        {
            return Result.Failure<ReportTable>(new Error(ReportErrors.CustomerRequired));
        }

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return Result.Failure<ReportTable>(new Error(CommonErrors.NotFound));
        }

        var broughtForward = await db.CustomerAccountEntries.AsNoTracking()
            .Where(e => e.CustomerId == id && e.OccurredAtUtc < period.FromUtc)
            .OrderByDescending(e => e.Id).Select(e => (decimal?)e.BalanceAfter).FirstOrDefaultAsync(cancellationToken) ?? 0m;
        var entries = await db.CustomerAccountEntries.AsNoTracking()
            .Where(e => e.CustomerId == id && e.OccurredAtUtc >= period.FromUtc && e.OccurredAtUtc < period.ToUtc)
            .OrderBy(e => e.Id)
            .Select(e => new { e.OccurredAtUtc, Type = e.Type.ToString(), e.Amount, e.BalanceAfter, e.Note })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var filters = PeriodFilters(period);
        filters.Insert(0, new("Reports.Filter.Customer", customer));
        return StatementTable(
            ReportKey.CustomerStatement, filters, broughtForward,
            entries.Select(e => (Local(e.OccurredAtUtc), $"CustomerEntryTypes.{e.Type}", e.Amount, e.BalanceAfter, e.Note)).ToList());
    }

    private async Task<Result<ReportTable>> SupplierStatementAsync(ReportPeriod period, int? supplierId, CancellationToken cancellationToken)
    {
        if (supplierId is not { } id)
        {
            return Result.Failure<ReportTable>(new Error(ReportErrors.SupplierRequired));
        }

        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<ReportTable>(new Error(CommonErrors.NotFound));
        }

        var broughtForward = await db.SupplierAccountEntries.AsNoTracking()
            .Where(e => e.SupplierId == id && e.OccurredAtUtc < period.FromUtc)
            .OrderByDescending(e => e.Id).Select(e => (decimal?)e.BalanceAfter).FirstOrDefaultAsync(cancellationToken) ?? 0m;
        var entries = await db.SupplierAccountEntries.AsNoTracking()
            .Where(e => e.SupplierId == id && e.OccurredAtUtc >= period.FromUtc && e.OccurredAtUtc < period.ToUtc)
            .OrderBy(e => e.Id)
            .Select(e => new { e.OccurredAtUtc, Type = e.Type.ToString(), e.Amount, e.BalanceAfter, e.Note })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var filters = PeriodFilters(period);
        filters.Insert(0, new("Reports.Filter.Supplier", supplier));
        return StatementTable(
            ReportKey.SupplierStatement, filters, broughtForward,
            entries.Select(e => (Local(e.OccurredAtUtc), $"SupplierEntryTypes.{e.Type}", e.Amount, e.BalanceAfter, e.Note)).ToList());
    }

    // A statement: the balance brought forward, then each entry as an increase or a decrease with the running balance.
    private static Result<ReportTable> StatementTable(
        ReportKey key, List<ReportFilter> filters, decimal broughtForward,
        List<(DateTime At, string TypeKey, decimal Amount, decimal BalanceAfter, string? Note)> entries)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { null, new ReportText("Reports.BroughtForward"), null, null, null, broughtForward },
        };
        rows.AddRange(entries.Select(e => (IReadOnlyList<object?>)new object?[]
        {
            e.At, new ReportText(e.TypeKey), e.Note, e.Amount > 0 ? e.Amount : null, e.Amount < 0 ? -e.Amount : null, e.BalanceAfter,
        }));

        var result = Done(key, filters,
            [Col("DateTime", ReportColumnKind.DateTime), Col("EntryType"), Col("Note"), Col("Increase", ReportColumnKind.Money),
             Col("Decrease", ReportColumnKind.Money), Col("Balance", ReportColumnKind.Money)],
            rows);
        return result.IsFailure
            ? result
            : Result.Success(result.Value with
            {
                Totals =
                [
                    new ReportText("Reports.TotalRow"), null, null, entries.Where(e => e.Amount > 0).Sum(e => e.Amount),
                    entries.Where(e => e.Amount < 0).Sum(e => -e.Amount), entries.Count > 0 ? entries[^1].BalanceAfter : broughtForward,
                ],
            });
    }
}
