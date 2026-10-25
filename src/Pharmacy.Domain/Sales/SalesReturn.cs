using Pharmacy.Domain.Common;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Domain.Sales;

/// <summary>The part of a returned line that goes back into one batch (the batch it was sold from).</summary>
public sealed record NewReturnAllocation(int InvoiceAllocationId, int BatchId, int BaseQuantity, decimal UnitCost, bool Quarantined);

/// <summary>One returned line as planned: how much, what it is worth, and which batches take it back.</summary>
public sealed record NewReturnLine(
    int InvoiceLineId,
    int ProductId,
    int ProductUnitId,
    int UnitFactor,
    int Quantity,
    int BaseQuantity,
    decimal Value,
    IReadOnlyList<NewReturnAllocation> Allocations);

public sealed class SalesReturnAllocation
{
    // Used by EF Core.
    private SalesReturnAllocation()
    {
    }

    internal SalesReturnAllocation(NewReturnAllocation allocation)
    {
        InvoiceAllocationId = allocation.InvoiceAllocationId;
        BatchId = allocation.BatchId;
        BaseQuantity = allocation.BaseQuantity;
        UnitCost = allocation.UnitCost;
        Quarantined = allocation.Quarantined;
    }

    public int Id { get; private set; }

    public int ReturnLineId { get; private set; }

    /// <summary>The sale's allocation this part gives back, so what was already returned is known per batch.</summary>
    public int InvoiceAllocationId { get; private set; }

    public int BatchId { get; private set; }

    public int BaseQuantity { get; private set; }

    /// <summary>The cost the goods had when they were sold; the same cost comes back with them.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>True when the goods went to quarantine instead of back on the shelf.</summary>
    public bool Quarantined { get; private set; }
}

public sealed class SalesReturnLine
{
    private readonly List<SalesReturnAllocation> _allocations = [];

    // Used by EF Core.
    private SalesReturnLine()
    {
    }

    internal SalesReturnLine(NewReturnLine line)
    {
        InvoiceLineId = line.InvoiceLineId;
        ProductId = line.ProductId;
        ProductUnitId = line.ProductUnitId;
        UnitFactor = line.UnitFactor;
        Quantity = line.Quantity;
        BaseQuantity = line.BaseQuantity;
        Value = line.Value;
        _allocations.AddRange(line.Allocations.Select(a => new SalesReturnAllocation(a)));
    }

    public int Id { get; private set; }

    public int ReturnId { get; private set; }

    public int InvoiceLineId { get; private set; }

    public int ProductId { get; private set; }

    public int ProductUnitId { get; private set; }

    public int UnitFactor { get; private set; }

    /// <summary>Quantity in the unit it was returned in.</summary>
    public int Quantity { get; private set; }

    public int BaseQuantity { get; private set; }

    /// <summary>What the customer gets back for the line.</summary>
    public decimal Value { get; private set; }

    public IReadOnlyList<SalesReturnAllocation> Allocations => _allocations;
}

/// <summary>
/// A posted sales return: what came back, what it was worth, and how it was settled (off the customer's debt and/or
/// in cash from the drawer). Returns are never edited or deleted.
/// </summary>
public sealed class SalesReturn
{
    public const int NumberMaxLength = 30;
    public const int ReasonMaxLength = 300;
    public const int MaxLines = 200;

    private readonly List<SalesReturnLine> _lines = [];

    // Used by EF Core.
    private SalesReturn()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public int InvoiceId { get; private set; }

    public int CustomerId { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>The part taken off what the customer owes.</summary>
    public decimal DebtReduction { get; private set; }

    /// <summary>The part paid back in cash from the drawer.</summary>
    public decimal CashRefund { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>True when the return came after the normal return period and needed the late-return permission.</summary>
    public bool IsLate { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }

    public IReadOnlyList<SalesReturnLine> Lines => _lines;

    public static Result<SalesReturn> Create(
        string number,
        int invoiceId,
        int customerId,
        IReadOnlyList<NewReturnLine> lines,
        RefundSplit split,
        string? reason,
        bool isLate,
        Guid operationId,
        DateTime createdAtUtc,
        int? userId,
        string userName)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(split);

        if (lines.Count == 0)
        {
            return Result.Failure<SalesReturn>(new Error(SalesErrors.ReturnNoLines));
        }

        if (lines.Count > MaxLines)
        {
            return Result.Failure<SalesReturn>(new Error(SalesErrors.TooManyLines, MaxLines));
        }

        var cleanReason = reason?.Trim();
        if (cleanReason?.Length > ReasonMaxLength)
        {
            return Result.Failure<SalesReturn>(new Error(SalesErrors.FieldTooLong));
        }

        if (isLate && string.IsNullOrEmpty(cleanReason))
        {
            return Result.Failure<SalesReturn>(new Error(SalesErrors.LateReturnReasonRequired));
        }

        var total = lines.Sum(l => l.Value);
        if (total <= 0 || split.DebtReduction + split.CashRefund != total)
        {
            return Result.Failure<SalesReturn>(new Error(SalesErrors.InvalidReturnAmount));
        }

        var salesReturn = new SalesReturn
        {
            Number = number,
            InvoiceId = invoiceId,
            CustomerId = customerId,
            Total = total,
            DebtReduction = split.DebtReduction,
            CashRefund = split.CashRefund,
            Reason = string.IsNullOrEmpty(cleanReason) ? null : cleanReason,
            IsLate = isLate,
            CreatedAtUtc = createdAtUtc,
            UserId = userId,
            UserName = userName.Length <= CustomerAccountEntry.UserNameMaxLength ? userName : userName[..CustomerAccountEntry.UserNameMaxLength],
            OperationId = operationId,
        };
        salesReturn._lines.AddRange(lines.Select(l => new SalesReturnLine(l)));
        return Result.Success(salesReturn);
    }
}
