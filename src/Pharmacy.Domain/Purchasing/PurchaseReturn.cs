using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Purchasing;

/// <summary>
/// One returned line as planned: how much goes back to the supplier, from which batch, from the shelf or from
/// quarantine, and what it is worth (the cost on the supplier's invoice).
/// </summary>
public sealed record NewPurchaseReturnLine(
    int InvoiceLineId,
    int ProductId,
    int ProductUnitId,
    int UnitFactor,
    int Quantity,
    int BaseQuantity,
    int BatchId,
    bool FromQuarantine,
    decimal Value);

public sealed class PurchaseReturnLine
{
    // Used by EF Core.
    private PurchaseReturnLine()
    {
    }

    internal PurchaseReturnLine(NewPurchaseReturnLine line)
    {
        InvoiceLineId = line.InvoiceLineId;
        ProductId = line.ProductId;
        ProductUnitId = line.ProductUnitId;
        UnitFactor = line.UnitFactor;
        Quantity = line.Quantity;
        BaseQuantity = line.BaseQuantity;
        BatchId = line.BatchId;
        FromQuarantine = line.FromQuarantine;
        Value = line.Value;
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

    /// <summary>The batch the goods leave.</summary>
    public int BatchId { get; private set; }

    /// <summary>True when the goods were taken from quarantine instead of the shelf stock.</summary>
    public bool FromQuarantine { get; private set; }

    /// <summary>What the supplier owes back for the line.</summary>
    public decimal Value { get; private set; }
}

/// <summary>
/// A posted return of goods to a supplier: what left, what it was worth, and how it was settled (off what the
/// pharmacy owes the supplier and/or in cash the supplier hands back). Returns are never edited or deleted.
/// </summary>
public sealed class PurchaseReturn
{
    public const int NumberMaxLength = 30;
    public const int ReasonMaxLength = 300;
    public const int MaxLines = 200;

    private readonly List<PurchaseReturnLine> _lines = [];

    // Used by EF Core.
    private PurchaseReturn()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public int InvoiceId { get; private set; }

    /// <summary>The registered supplier; empty when the invoice was a cash purchase from an unregistered seller.</summary>
    public int? SupplierId { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>The part taken off what the pharmacy owes the supplier.</summary>
    public decimal DebtReduction { get; private set; }

    /// <summary>The part the supplier hands back in cash, received into the open shift.</summary>
    public decimal CashRefund { get; private set; }

    /// <summary>Why the goods go back (damaged, near expiry, wrong item, ...). Required.</summary>
    public string Reason { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }

    public IReadOnlyList<PurchaseReturnLine> Lines => _lines;

    public static Result<PurchaseReturn> Create(
        string number,
        int invoiceId,
        int? supplierId,
        IReadOnlyList<NewPurchaseReturnLine> lines,
        RefundSplit split,
        string? reason,
        Guid operationId,
        DateTime createdAtUtc,
        int? userId,
        string userName)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(split);

        if (lines.Count == 0)
        {
            return Result.Failure<PurchaseReturn>(new Error(PurchasingErrors.ReturnNoLines));
        }

        if (lines.Count > MaxLines)
        {
            return Result.Failure<PurchaseReturn>(new Error(PurchasingErrors.TooManyLines, MaxLines));
        }

        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason))
        {
            return Result.Failure<PurchaseReturn>(new Error(PurchasingErrors.ReturnReasonRequired));
        }

        if (cleanReason.Length > ReasonMaxLength)
        {
            return Result.Failure<PurchaseReturn>(new Error(PurchasingErrors.FieldTooLong));
        }

        var total = lines.Sum(l => l.Value);
        if (total <= 0 || split.DebtReduction + split.CashRefund != total || (supplierId is null && split.DebtReduction != 0))
        {
            return Result.Failure<PurchaseReturn>(new Error(PurchasingErrors.InvalidReturnAmount));
        }

        var purchaseReturn = new PurchaseReturn
        {
            Number = number,
            InvoiceId = invoiceId,
            SupplierId = supplierId,
            Total = total,
            DebtReduction = split.DebtReduction,
            CashRefund = split.CashRefund,
            Reason = cleanReason,
            CreatedAtUtc = createdAtUtc,
            UserId = userId,
            UserName = userName.Length <= SupplierAccountEntry.UserNameMaxLength ? userName : userName[..SupplierAccountEntry.UserNameMaxLength],
            OperationId = operationId,
        };
        purchaseReturn._lines.AddRange(lines.Select(l => new PurchaseReturnLine(l)));
        return Result.Success(purchaseReturn);
    }
}
