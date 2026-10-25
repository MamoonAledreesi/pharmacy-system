using Pharmacy.Application.Catalog;

namespace Pharmacy.Application.Sales;

/// <summary>One line of a sale as the seller enters it: <c>Quantity</c> and <c>UnitPrice</c> are in the chosen packaging unit.</summary>
public sealed record SaleLineRequest(int ProductId, int ProductUnitId, int Quantity, decimal UnitPrice);

/// <summary>A supervisor's own user name and password, entered in the approval dialog. Never stored.</summary>
public sealed record SupervisorCredentials(string? UserName, string? Password)
{
    public bool IsProvided => !string.IsNullOrWhiteSpace(UserName) || !string.IsNullOrEmpty(Password);
}

/// <summary>
/// A sale. The discount is either an amount or a percentage of the subtotal (not both). <c>PaidCash</c> goes into
/// the open shift and the rest stays owed by the customer, which needs a named customer. <c>BelowCostReason</c> is
/// required when lines sell below cost. <c>Supervisor</c> is needed only when the seller lacks a permission the sale
/// requires. <c>OperationId</c> is generated once per form so a double click or a retry posts the sale only once.
/// </summary>
public sealed record SaleRequest(
    Guid OperationId,
    int CustomerId,
    IReadOnlyList<SaleLineRequest> Lines,
    decimal? DiscountAmount,
    decimal? DiscountPercent,
    decimal PaidCash,
    string? Note,
    string? BelowCostReason,
    SupervisorCredentials? Supervisor);

public sealed record SalePreviewRequest(IReadOnlyList<SaleLineRequest> Lines, decimal? DiscountAmount, decimal? DiscountPercent);

/// <summary>
/// What the seller sees before posting: per line whether the stock is there, whether it comes from a batch that is
/// near expiry, and whether it would sell below cost; and the invoice totals with whether the discount needs approval.
/// Costs themselves are never exposed here.
/// </summary>
public sealed record SalePreviewLine(
    bool InStock, int SellableBaseQuantity, bool HasNearExpiry, bool BelowCost, decimal DiscountShare, decimal NetTotal);

public sealed record SalePreview(
    IReadOnlyList<SalePreviewLine> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal DiscountPercent,
    decimal Total,
    decimal MaxDiscountPercent,
    bool DiscountNeedsApproval,
    bool AnyBelowCost,
    bool AllInStock);

public sealed record SaleQuery(string? Text, int? CustomerId, DateOnly? From, DateOnly? To, int PageIndex, int PageSize);

public sealed record SaleListItem(
    int Id, string Number, DateTime CreatedAtUtc, string CustomerName, decimal Total, decimal PaidCash, decimal OnCredit, int LineCount);

public sealed record SaleLineDetail(
    LocalizedName Product,
    string? Strength,
    LocalizedName Unit,
    int Quantity,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal DiscountShare,
    decimal NetTotal,
    decimal? CostTotal,
    bool IsBelowCost,
    IReadOnlyList<SaleAllocationDetail> Batches);

public sealed record SaleAllocationDetail(string BatchNumber, DateOnly? ExpiryDate, int BaseQuantity, bool IsNearExpiry);

public sealed record SaleDetail(
    int Id,
    string Number,
    DateTime CreatedAtUtc,
    int CustomerId,
    string CustomerName,
    string? CustomerPhone,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DiscountPercent,
    decimal Total,
    decimal PaidCash,
    decimal OnCredit,
    string? Note,
    string UserName,
    string? BelowCostApprovedBy,
    string? BelowCostReason,
    string? DiscountApprovedBy,
    bool CostVisible,
    IReadOnlyList<SaleLineDetail> Lines);
