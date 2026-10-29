using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Reports;

/// <summary>The building blocks of profit for one period; the profit report and the dashboard both show these.</summary>
public sealed record ProfitFigures(
    decimal Sales, decimal Returned, decimal SalesCost, decimal ReturnedCost, decimal Expenses, decimal WriteOffLoss, decimal AdjustmentShortfall)
{
    public decimal NetSales => Sales - Returned;

    public decimal CostOfGoods => SalesCost - ReturnedCost;

    public decimal GrossProfit => NetSales - CostOfGoods;

    public decimal OperatingProfit => GrossProfit - Expenses - WriteOffLoss - AdjustmentShortfall;
}

/// <summary>
/// Works out profit. Sales and returns count by their own dates, costs are the cost recorded when the goods were sold,
/// and purchases, purchase returns and manual cash movements never touch profit. No permission check: callers decide who may see it.
/// </summary>
public sealed class ProfitCalculator(IPharmacyDbContext db)
{
    private static readonly StockMovementType[] _writeOffAndAdjustmentTypes =
    [
        StockMovementType.WriteOff, StockMovementType.WriteOffQuarantined,
        StockMovementType.AdjustmentIncrease, StockMovementType.AdjustmentDecrease,
    ];

    /// <summary>Instants are the UTC bounds of the period (end exclusive); days are the same period as local dates (both inclusive).</summary>
    public async Task<ProfitFigures> ComputeAsync(
        DateTime fromUtc, DateTime toUtc, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default)
    {
        var invoices = db.SalesInvoices.AsNoTracking().Where(i => i.CreatedAtUtc >= fromUtc && i.CreatedAtUtc < toUtc);
        var returns = db.SalesReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc);

        var sales = await invoices.SumAsync(i => (decimal?)i.Total, cancellationToken) ?? 0m;
        var salesCost = await invoices.SelectMany(i => i.Lines).SumAsync(l => (decimal?)l.CostTotal, cancellationToken) ?? 0m;
        var returned = await returns.SumAsync(r => (decimal?)r.Total, cancellationToken) ?? 0m;
        var returnedCost = await returns.SelectMany(r => r.Lines).SelectMany(l => l.Allocations)
            .SumAsync(a => (decimal?)(a.BaseQuantity * a.UnitCost), cancellationToken) ?? 0m;
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => !e.IsReversed && e.ExpenseDate >= fromDay && e.ExpenseDate <= toDay)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var stockEffects = await db.StockMovements.AsNoTracking()
            .Where(m => m.OccurredAtUtc >= fromUtc && m.OccurredAtUtc < toUtc && _writeOffAndAdjustmentTypes.Contains(m.Type))
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key, Value = g.Sum(m => (m.AvailableDelta + m.QuarantineDelta) * m.UnitCost) })
            .ToDictionaryAsync(x => x.Type, x => x.Value, cancellationToken);
        decimal ValueOf(StockMovementType type) => stockEffects.GetValueOrDefault(type);

        return new ProfitFigures(
            sales, returned, salesCost, returnedCost, expenses,
            -(ValueOf(StockMovementType.WriteOff) + ValueOf(StockMovementType.WriteOffQuarantined)),
            -(ValueOf(StockMovementType.AdjustmentDecrease) + ValueOf(StockMovementType.AdjustmentIncrease)));
    }
}
