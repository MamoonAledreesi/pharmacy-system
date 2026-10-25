using Pharmacy.Application.Catalog;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Inventory;

public enum StockFilter
{
    All = 0,
    InStock = 1,
    OutOfStock = 2,
    LowStock = 3,
    HasExpired = 4,
    NearExpiry = 5,
    HasQuarantined = 6,
}

/// <summary>Stock list filter. <see cref="Text"/> matches trade names, scientific name, and barcodes.</summary>
public sealed record StockQuery(string? Text, StockFilter Filter, int PageIndex, int PageSize);

/// <summary>
/// Stock totals of one product, in base units. <c>Expired</c> and <c>NearExpiry</c> count sellable-column
/// quantities of expired and soon-to-expire batches. <c>StockValue</c> is null unless the user may see costs.
/// </summary>
public sealed record StockListItem(
    int ProductId,
    string? TradeNameArabic,
    string? TradeNameEnglish,
    string? Strength,
    bool IsActive,
    int Available,
    int Quarantined,
    int Expired,
    int NearExpiry,
    int? MinimumStock,
    decimal? StockValue,
    IReadOnlyList<ProductUnitSummary> Units)
{
    public string DisplayName(bool arabic) =>
        (arabic ? TradeNameArabic ?? TradeNameEnglish : TradeNameEnglish ?? TradeNameArabic) ?? string.Empty;

    /// <summary>Sellable today: available stock minus the expired part.</summary>
    public int Sellable => Available - Expired;

    public bool IsLow => MinimumStock is { } minimum && Sellable <= minimum;
}

/// <summary>One batch as shown to the user. <c>UnitCost</c> is null unless the user may see costs.</summary>
public sealed record BatchRow(
    int Id,
    string BatchNumber,
    DateOnly? ExpiryDate,
    ExpiryStatus Status,
    DateTime ReceivedAtUtc,
    int Available,
    int Quarantined,
    decimal? UnitCost);

public sealed record ProductStockDetail(
    int ProductId,
    string? TradeNameArabic,
    string? TradeNameEnglish,
    string? Strength,
    bool TracksExpiry,
    bool IsActive,
    int? MinimumStock,
    int NearExpiryDays,
    IReadOnlyList<ProductUnitSummary> Units,
    IReadOnlyList<BatchRow> Batches)
{
    public string DisplayName(bool arabic) =>
        (arabic ? TradeNameArabic ?? TradeNameEnglish : TradeNameEnglish ?? TradeNameArabic) ?? string.Empty;
}

public sealed record MovementQuery(
    int ProductId, int? BatchId, StockMovementType? Type, DateTime? FromUtc, DateTime? ToUtc, int PageIndex, int PageSize);

public sealed record MovementRow(
    long Id,
    DateTime OccurredAtUtc,
    StockMovementType Type,
    string BatchNumber,
    DateOnly? ExpiryDate,
    int AvailableDelta,
    int QuarantineDelta,
    int AvailableAfter,
    int QuarantineAfter,
    decimal? UnitCost,
    string? ReasonCode,
    string? Note,
    string UserName);
