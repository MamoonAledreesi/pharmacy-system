using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Reports;

public sealed partial class ReportService
{
    private static readonly StockMovementType[] _writeOffAndAdjustmentTypes =
    [
        StockMovementType.WriteOff, StockMovementType.WriteOffQuarantined,
        StockMovementType.AdjustmentIncrease, StockMovementType.AdjustmentDecrease,
    ];

    private bool CanSeeCost => currentUser.HasPermission(Permissions.ReportsViewCostAndProfit);

    private static List<ReportFilter> TextFilters(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [new("Reports.Filter.Text", text.Trim())];

    private static ReportText ExpiryText(ExpiryStatus status) => new(status switch
    {
        ExpiryStatus.Expired => "Expiry.Expired",
        ExpiryStatus.NearExpiry => "Expiry.NearExpiry",
        ExpiryStatus.NoExpiry => "Reports.Expiry.NoExpiry",
        _ => "Reports.Expiry.Ok",
    });

    private Task<Result<ReportTable>> StockByBatchAsync(string? text, CancellationToken cancellationToken) =>
        StockRowsAsync(ReportKey.StockByBatch, text, expiringOnly: false, cancellationToken);

    private Task<Result<ReportTable>> ExpiryStatusAsync(string? text, CancellationToken cancellationToken) =>
        StockRowsAsync(ReportKey.ExpiryStatus, text, expiringOnly: true, cancellationToken);

    // Both stock reports list batches that still hold stock; the expiry report keeps only the expired and near-expiry ones.
    private async Task<Result<ReportTable>> StockRowsAsync(ReportKey key, string? text, bool expiringOnly, CancellationToken cancellationToken)
    {
        var today = Today;
        var defaultNearDays = await db.PharmacySettings.Select(s => s.NearExpiryDays).SingleAsync(cancellationToken);
        var batches =
            from p in ProductSearchFilter.Apply(db.Products.AsNoTracking(), text, includeInactive: true)
            join b in db.Batches.AsNoTracking() on p.Id equals b.ProductId
            where b.QuantityAvailable + b.QuantityQuarantined > 0
            select new { p.TradeNameArabic, p.TradeNameEnglish, p.NearExpiryDays, b.BatchNumber, b.ExpiryDate, b.QuantityAvailable, b.QuantityQuarantined, b.UnitCost };

        if (expiringOnly)
        {
            var maxNearDays = Math.Max(defaultNearDays, await db.Products.MaxAsync(p => p.NearExpiryDays, cancellationToken) ?? 0);
            var horizon = today.AddDays(maxNearDays);
            batches = batches.Where(b => b.ExpiryDate != null && b.ExpiryDate <= horizon);
        }

        var found = await batches
            .OrderBy(b => b.TradeNameEnglish ?? b.TradeNameArabic).ThenBy(b => b.ExpiryDate).ThenBy(b => b.BatchNumber)
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var showCost = CanSeeCost;
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var b in found)
        {
            var status = ExpiryRules.StatusOf(b.ExpiryDate, today, b.NearExpiryDays ?? defaultNearDays);
            if (expiringOnly && status is not (ExpiryStatus.Expired or ExpiryStatus.NearExpiry))
            {
                continue;
            }

            var row = new List<object?>
            {
                ProductName(b.TradeNameArabic, b.TradeNameEnglish), b.BatchNumber, b.ExpiryDate, ExpiryText(status),
                b.ExpiryDate is { } expiry ? expiry.DayNumber - today.DayNumber : null,
                b.QuantityAvailable, b.QuantityQuarantined,
            };
            if (showCost)
            {
                row.Add(b.UnitCost);
                row.Add((b.QuantityAvailable + b.QuantityQuarantined) * b.UnitCost);
            }

            rows.Add(row);
        }

        var columns = new List<ReportColumn>
        {
            Col("Product"), Col("BatchNumber"), Col("ExpiryDate", ReportColumnKind.Date), Col("ExpiryStatus"),
            Col("DaysLeft", ReportColumnKind.Integer), Col("Available", ReportColumnKind.Integer), Col("Quarantined", ReportColumnKind.Integer),
        };
        var sums = new List<int> { 5, 6 };
        if (showCost)
        {
            columns.Add(Col("UnitCost", ReportColumnKind.Money));
            columns.Add(Col("StockValue", ReportColumnKind.Money));
            sums.Add(8);
        }

        return Done(key, TextFilters(text), [.. columns], rows, [.. sums]);
    }

    private async Task<Result<ReportTable>> WriteOffsAndAdjustmentsAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var movements = await (
                from m in db.StockMovements.AsNoTracking()
                where m.OccurredAtUtc >= period.FromUtc && m.OccurredAtUtc < period.ToUtc && _writeOffAndAdjustmentTypes.Contains(m.Type)
                join p in db.Products on m.ProductId equals p.Id
                join b in db.Batches on m.BatchId equals b.Id
                orderby m.OccurredAtUtc, m.Id
                select new
                {
                    m.OccurredAtUtc, m.Type, p.TradeNameArabic, p.TradeNameEnglish, b.BatchNumber,
                    Quantity = m.AvailableDelta + m.QuarantineDelta, m.UnitCost, m.ReasonCode, m.UserName, m.Note,
                })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var showCost = CanSeeCost;
        var rows = movements
            .Select(m =>
            {
                var row = new List<object?>
                {
                    Local(m.OccurredAtUtc), new ReportText($"StockMovementTypes.{m.Type}"), ProductName(m.TradeNameArabic, m.TradeNameEnglish),
                    m.BatchNumber, m.Quantity, ReasonOf(m.ReasonCode), m.UserName, m.Note,
                };
                if (showCost)
                {
                    row.Add(m.Quantity * m.UnitCost);
                }

                return (IReadOnlyList<object?>)row;
            })
            .ToList();

        var columns = new List<ReportColumn>
        {
            Col("DateTime", ReportColumnKind.DateTime), Col("MovementType"), Col("Product"), Col("BatchNumber"),
            Col("QuantityChange", ReportColumnKind.Integer), Col("Reason"), Col("User"), Col("Note"),
        };
        var sums = new List<int> { 4 };
        if (showCost)
        {
            columns.Add(Col("ValueChange", ReportColumnKind.Money));
            sums.Add(8);
        }

        return Done(ReportKey.WriteOffsAndAdjustments, PeriodFilters(period), [.. columns], rows, [.. sums]);
    }

    private static object? ReasonOf(string? code) =>
        code is null ? null : StockReasons.All.Contains(code) ? new ReportText($"StockReasons.{code}") : code;
}
