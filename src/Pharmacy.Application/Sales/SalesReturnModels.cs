using Pharmacy.Application.Catalog;

namespace Pharmacy.Application.Sales;

/// <summary>
/// One line to give back: which line of the original invoice, how many in which unit, and whether the goods are fit to
/// sell again (<c>Sellable</c>) or must go to quarantine (the default: damaged or expired goods).
/// </summary>
public sealed record ReturnLineRequest(int InvoiceLineId, int ProductUnitId, int Quantity, bool Sellable);

/// <summary>
/// A sales return against one invoice. <c>Reason</c> is optional, except after the return period. <c>OperationId</c>
/// is generated once per form so a double click or a retry posts the return only once.
/// </summary>
public sealed record SalesReturnRequest(Guid OperationId, int InvoiceId, IReadOnlyList<ReturnLineRequest> Lines, string? Reason);

/// <summary>What the return would do, shown for confirmation before it is posted.</summary>
public sealed record ReturnPreview(
    decimal Total, decimal DebtReduction, decimal CashRefund, bool IsLate, int WindowDays, IReadOnlyList<decimal> LineValues);

public sealed record ReturnableLine(
    int InvoiceLineId,
    LocalizedName Product,
    string? Strength,
    LocalizedName SoldUnit,
    int SoldQuantity,
    int SoldBaseQuantity,
    int ReturnedBaseQuantity,
    int RemainingBaseQuantity,
    decimal NetTotal,
    IReadOnlyList<ProductUnitSummary> Units);

/// <summary>An invoice with what can still be returned from each of its lines, and whether the return period is over.</summary>
public sealed record ReturnableInvoice(
    int InvoiceId,
    string Number,
    DateTime CreatedAtUtc,
    int CustomerId,
    string CustomerName,
    bool IsDefaultCash,
    decimal Total,
    decimal OnCredit,
    int WindowDays,
    int DaysSinceSale,
    bool IsLate,
    bool CanReturnLate,
    IReadOnlyList<ReturnableLine> Lines);

public sealed record ReturnQuery(string? Text, int? InvoiceId, DateOnly? From, DateOnly? To, int PageIndex, int PageSize);

public sealed record ReturnListItem(
    int Id,
    string Number,
    DateTime CreatedAtUtc,
    int InvoiceId,
    string InvoiceNumber,
    string CustomerName,
    decimal Total,
    decimal DebtReduction,
    decimal CashRefund,
    bool IsLate);

public sealed record ReturnLineDetail(
    LocalizedName Product, string? Strength, LocalizedName Unit, int Quantity, decimal Value, bool AnyQuarantined, bool AnySellable);

public sealed record ReturnDetail(
    int Id,
    string Number,
    DateTime CreatedAtUtc,
    int InvoiceId,
    string InvoiceNumber,
    int CustomerId,
    string CustomerName,
    decimal Total,
    decimal DebtReduction,
    decimal CashRefund,
    string? Reason,
    bool IsLate,
    string UserName,
    IReadOnlyList<ReturnLineDetail> Lines);
