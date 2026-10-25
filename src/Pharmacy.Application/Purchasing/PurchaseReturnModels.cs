using Pharmacy.Application.Catalog;

namespace Pharmacy.Application.Purchasing;

/// <summary>
/// One line to send back: which line of the supplier invoice, how many in which unit, and whether the goods come from
/// the shelf stock (the default) or from quarantine (for example damaged goods put aside earlier).
/// </summary>
public sealed record PurchaseReturnLineRequest(int InvoiceLineId, int ProductUnitId, int Quantity, bool FromQuarantine);

/// <summary>
/// A return of goods to a supplier against one purchase invoice. <c>Reason</c> is required. <c>OperationId</c> is
/// generated once per form so a double click or a retry posts the return only once.
/// </summary>
public sealed record PurchaseReturnRequest(Guid OperationId, int InvoiceId, IReadOnlyList<PurchaseReturnLineRequest> Lines, string? Reason);

/// <summary>What the return would do, shown for confirmation before it is posted.</summary>
public sealed record PurchaseReturnPreview(decimal Total, decimal DebtReduction, decimal CashRefund, IReadOnlyList<decimal> LineValues);

public sealed record PurchaseReturnableLine(
    int InvoiceLineId,
    LocalizedName Product,
    string? Strength,
    LocalizedName ReceivedUnit,
    int ReceivedQuantity,
    int ReceivedBaseQuantity,
    int ReturnedBaseQuantity,
    int RemainingBaseQuantity,
    decimal LineTotal,
    string BatchNumber,
    DateOnly? ExpiryDate,
    int BatchAvailable,
    int BatchQuarantined,
    IReadOnlyList<ProductUnitSummary> Units);

/// <summary>A purchase invoice with, for each line, what was received, what went back already, and what the batch still holds.</summary>
public sealed record PurchaseReturnableInvoice(
    int InvoiceId,
    string Number,
    DateOnly InvoiceDate,
    int? SupplierId,
    string? SupplierName,
    decimal Total,
    decimal OnCredit,
    IReadOnlyList<PurchaseReturnableLine> Lines);

public sealed record PurchaseReturnQuery(string? Text, int? InvoiceId, DateOnly? From, DateOnly? To, int PageIndex, int PageSize);

public sealed record PurchaseReturnListItem(
    int Id,
    string Number,
    DateTime CreatedAtUtc,
    int InvoiceId,
    string InvoiceNumber,
    string? SupplierName,
    decimal Total,
    decimal DebtReduction,
    decimal CashRefund);

public sealed record PurchaseReturnLineDetail(
    LocalizedName Product, string? Strength, LocalizedName Unit, int Quantity, string BatchNumber, bool FromQuarantine, decimal Value);

public sealed record PurchaseReturnDetail(
    int Id,
    string Number,
    DateTime CreatedAtUtc,
    int InvoiceId,
    string InvoiceNumber,
    int? SupplierId,
    string? SupplierName,
    decimal Total,
    decimal DebtReduction,
    decimal CashRefund,
    string Reason,
    string UserName,
    IReadOnlyList<PurchaseReturnLineDetail> Lines);
