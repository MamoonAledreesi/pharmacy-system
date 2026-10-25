using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Domain.Sales;

/// <summary>One line as entered: <c>Quantity</c> and <c>UnitPrice</c> are in the chosen packaging unit (for example boxes).</summary>
public sealed record NewSaleLine(int ProductId, int ProductUnitId, int UnitFactor, int Quantity, int BaseQuantity, decimal UnitPrice);

/// <summary>The part of a sold line taken from one batch, with that batch's cost at the time of the sale.</summary>
public sealed class SalesLineAllocation
{
    // Used by EF Core.
    private SalesLineAllocation()
    {
    }

    internal SalesLineAllocation(StockAllocation allocation)
    {
        BatchId = allocation.BatchId;
        BaseQuantity = allocation.Quantity;
        UnitCost = allocation.UnitCost;
        IsNearExpiry = allocation.IsNearExpiry;
    }

    public int Id { get; private set; }

    public int LineId { get; private set; }

    public int BatchId { get; private set; }

    /// <summary>Quantity taken from the batch, in the product's base unit.</summary>
    public int BaseQuantity { get; private set; }

    /// <summary>Cost of one base unit of the batch, kept for sales returns and profit.</summary>
    public decimal UnitCost { get; private set; }

    public bool IsNearExpiry { get; private set; }
}

public sealed class SalesInvoiceLine
{
    private readonly List<SalesLineAllocation> _allocations = [];

    // Used by EF Core.
    private SalesInvoiceLine()
    {
    }

    internal SalesInvoiceLine(NewSaleLine line, decimal subtotal, decimal discountShare)
    {
        ProductId = line.ProductId;
        ProductUnitId = line.ProductUnitId;
        UnitFactor = line.UnitFactor;
        Quantity = line.Quantity;
        BaseQuantity = line.BaseQuantity;
        UnitPrice = line.UnitPrice;
        LineSubtotal = subtotal;
        DiscountShare = discountShare;
        NetTotal = subtotal - discountShare;
    }

    public int Id { get; private set; }

    public int InvoiceId { get; private set; }

    public int ProductId { get; private set; }

    public int ProductUnitId { get; private set; }

    /// <summary>How many base units one chosen unit holds, kept so the line stays readable if units change later.</summary>
    public int UnitFactor { get; private set; }

    /// <summary>Quantity in the chosen packaging unit.</summary>
    public int Quantity { get; private set; }

    public int BaseQuantity { get; private set; }

    /// <summary>Price of one chosen unit as the seller entered it.</summary>
    public decimal UnitPrice { get; private set; }

    public decimal LineSubtotal { get; private set; }

    /// <summary>This line's share of the invoice discount.</summary>
    public decimal DiscountShare { get; private set; }

    /// <summary>What the customer pays for the line: subtotal minus the discount share.</summary>
    public decimal NetTotal { get; private set; }

    /// <summary>What the sold goods cost the pharmacy: the sum of the allocations' quantities times their batch costs.</summary>
    public decimal CostTotal { get; private set; }

    public IReadOnlyList<SalesLineAllocation> Allocations => _allocations;

    public bool IsBelowCost => NetTotal < CostTotal;

    /// <summary>Records the batches the goods were taken from and what they cost.</summary>
    internal void RecordAllocations(IReadOnlyList<StockAllocation> allocations)
    {
        _allocations.Clear();
        _allocations.AddRange(allocations.Select(a => new SalesLineAllocation(a)));
        CostTotal = decimal.Round(allocations.Sum(a => a.Quantity * a.UnitCost), SalePricing.Decimals, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// A posted sale: what was sold (from which batches, at what cost), the discount, and how much was paid in cash
/// versus left owed by the customer. Invoices are never edited or deleted; a mistake is corrected with a sales return.
/// </summary>
public sealed class SalesInvoice
{
    public const int NumberMaxLength = 30;
    public const int NoteMaxLength = 300;
    public const int ReasonMaxLength = 300;
    public const int MaxLines = 200;

    private readonly List<SalesInvoiceLine> _lines = [];

    // Used by EF Core.
    private SalesInvoice()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal DiscountPercent { get; private set; }

    /// <summary>What the customer pays: the subtotal minus the discount.</summary>
    public decimal Total { get; private set; }

    public decimal PaidCash { get; private set; }

    /// <summary>The part that stays owed by the customer.</summary>
    public decimal OnCredit { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }

    /// <summary>The supervisor who allowed lines to sell below cost, when the seller could not do it alone.</summary>
    public string? BelowCostApprovedBy { get; private set; }

    public string? BelowCostReason { get; private set; }

    /// <summary>The supervisor who allowed a discount above the limit, when the seller could not do it alone.</summary>
    public string? DiscountApprovedBy { get; private set; }

    public IReadOnlyList<SalesInvoiceLine> Lines => _lines;

    public bool HasBelowCostLines => _lines.Any(l => l.IsBelowCost);

    public static Result<SalesInvoice> Create(
        string number,
        int customerId,
        PricedSale priced,
        IReadOnlyList<NewSaleLine> lines,
        decimal paidCash,
        string? note,
        Guid operationId,
        DateTime createdAtUtc,
        int? userId,
        string userName)
    {
        ArgumentNullException.ThrowIfNull(priced);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Result.Failure<SalesInvoice>(new Error(SalesErrors.NoLines));
        }

        if (lines.Count > MaxLines)
        {
            return Result.Failure<SalesInvoice>(new Error(SalesErrors.TooManyLines, MaxLines));
        }

        var cleanNote = note?.Trim();
        if (cleanNote?.Length > NoteMaxLength)
        {
            return Result.Failure<SalesInvoice>(new Error(SalesErrors.FieldTooLong));
        }

        if (paidCash < 0 || paidCash > priced.Net || decimal.Round(paidCash, SalePricing.Decimals) != paidCash)
        {
            return Result.Failure<SalesInvoice>(new Error(SalesErrors.InvalidPaidAmount));
        }

        var invoice = new SalesInvoice
        {
            Number = number,
            CustomerId = customerId,
            Subtotal = priced.Subtotal,
            DiscountAmount = priced.Discount,
            DiscountPercent = priced.DiscountPercent,
            Total = priced.Net,
            PaidCash = paidCash,
            OnCredit = priced.Net - paidCash,
            Note = string.IsNullOrEmpty(cleanNote) ? null : cleanNote,
            CreatedAtUtc = createdAtUtc,
            UserId = userId,
            UserName = userName.Length <= CustomerAccountEntry.UserNameMaxLength ? userName : userName[..CustomerAccountEntry.UserNameMaxLength],
            OperationId = operationId,
        };
        for (var index = 0; index < lines.Count; index++)
        {
            invoice._lines.Add(new SalesInvoiceLine(lines[index], priced.LineSubtotals[index], priced.DiscountShares[index]));
        }

        return Result.Success(invoice);
    }

    /// <summary>Records the batches a line's goods were taken from (in the order the lines were given).</summary>
    public void RecordAllocations(int lineIndex, IReadOnlyList<StockAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        _lines[lineIndex].RecordAllocations(allocations);
    }

    /// <summary>Notes who allowed what the seller could not allow alone.</summary>
    public Result RecordApprovals(string? belowCostApprovedBy, string? belowCostReason, string? discountApprovedBy)
    {
        var reason = belowCostReason?.Trim();
        if (reason?.Length > ReasonMaxLength)
        {
            return Result.Failure(new Error(SalesErrors.FieldTooLong));
        }

        BelowCostApprovedBy = belowCostApprovedBy;
        BelowCostReason = string.IsNullOrEmpty(reason) ? null : reason;
        DiscountApprovedBy = discountApprovedBy;
        return Result.Success();
    }
}
