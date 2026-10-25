using Pharmacy.Application.Catalog;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

public sealed record SupplierInput(string? Name, string? Phone, string? Address, string? Notes, bool IsActive);

public sealed record SupplierQuery(string? Text, bool IncludeInactive, int PageIndex, int PageSize);

public sealed record SupplierListItem(int Id, string Name, string? Phone, bool IsActive, decimal Balance);

/// <summary>A supplier for the details page and for editing. <c>RowVersion</c> detects two people editing at once.</summary>
public sealed record SupplierDetail(
    int Id, string Name, string? Phone, string? Address, string? Notes, bool IsActive, decimal Balance, bool HasOpeningBalance, byte[] RowVersion);

/// <summary>The statement of one supplier, newest entry first.</summary>
public sealed record StatementQuery(int SupplierId, int PageIndex, int PageSize);

/// <summary>
/// One statement line. <c>Amount</c> is positive when the pharmacy owes more and negative when it owes less.
/// <c>PaymentId</c> and <c>PaymentReversed</c> are set for payment lines so the page can offer a reversal.
/// </summary>
public sealed record SupplierEntryRow(
    long Id,
    DateTime OccurredAtUtc,
    SupplierEntryType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Note,
    string UserName,
    string? ReferenceType,
    long? ReferenceId,
    string? ReferenceNumber,
    bool PaymentReversed);

public sealed record PaySupplierRequest(Guid OperationId, int SupplierId, decimal Amount, string? Note);

public sealed record ReversePaymentRequest(Guid OperationId, int PaymentId, string? Reason);

public sealed record OpeningBalanceRequest(Guid OperationId, int SupplierId, decimal Amount, string? Note);

/// <summary>
/// One line of a purchase as the user enters it. <c>Quantity</c> and <c>CostPerUnit</c> are in the chosen packaging
/// unit. A blank batch number gets an automatic one; the expiry date is required for products that expire.
/// </summary>
public sealed record PurchaseLineRequest(
    int ProductId, int ProductUnitId, int Quantity, decimal CostPerUnit, string? BatchNumber, DateOnly? ExpiryDate);

/// <summary>
/// A purchase invoice. <c>PaidCash</c> comes out of the open shift; the rest stays owed to the supplier. A purchase
/// without a registered supplier must be paid in full. <c>OperationId</c> is generated once per form so a double
/// click or a retry posts the invoice only once.
/// </summary>
public sealed record PurchaseRequest(
    Guid OperationId,
    int? SupplierId,
    string? SupplierName,
    DateOnly InvoiceDate,
    string? SupplierInvoiceNumber,
    decimal PaidCash,
    string? Note,
    IReadOnlyList<PurchaseLineRequest> Lines);

public sealed record PurchaseQuery(string? Text, int? SupplierId, DateOnly? From, DateOnly? To, int PageIndex, int PageSize);

public sealed record PurchaseListItem(
    int Id, string Number, DateOnly InvoiceDate, string? SupplierName, decimal Total, decimal PaidCash, decimal OnCredit, int LineCount);

public sealed record PurchaseLineDetail(
    LocalizedName Product,
    string? Strength,
    LocalizedName Unit,
    int Quantity,
    int BaseQuantity,
    decimal CostPerUnit,
    decimal LineTotal,
    string BatchNumber,
    DateOnly? ExpiryDate);

public sealed record PurchaseDetail(
    int Id,
    string Number,
    DateOnly InvoiceDate,
    int? SupplierId,
    string? SupplierName,
    string? SupplierInvoiceNumber,
    decimal Total,
    decimal PaidCash,
    decimal OnCredit,
    string? Note,
    DateTime CreatedAtUtc,
    string UserName,
    IReadOnlyList<PurchaseLineDetail> Lines);

/// <summary>What the purchase line dialog needs about a product: its units, whether it expires, and its latest cost.</summary>
public sealed record PurchaseProduct(
    int ProductId,
    string? TradeNameArabic,
    string? TradeNameEnglish,
    string? Strength,
    bool TracksExpiry,
    IReadOnlyList<ProductUnitSummary> Units,
    decimal? LastBaseUnitCost)
{
    public string DisplayName(bool arabic) =>
        (arabic ? TradeNameArabic ?? TradeNameEnglish : TradeNameEnglish ?? TradeNameArabic) ?? string.Empty;
}
