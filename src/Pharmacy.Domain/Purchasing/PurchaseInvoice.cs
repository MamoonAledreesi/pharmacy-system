using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Domain.Purchasing;

/// <summary>
/// One line of a purchase as entered: <c>Quantity</c> and <c>CostPerUnit</c> are in the chosen packaging unit
/// (for example boxes), <c>BaseQuantity</c> and <c>BaseUnitCost</c> are the same goods in the product's base unit.
/// </summary>
public sealed record NewPurchaseLine(
    int ProductId,
    int ProductUnitId,
    int Quantity,
    int UnitFactor,
    int BaseQuantity,
    decimal CostPerUnit,
    decimal BaseUnitCost,
    string? BatchNumber,
    DateOnly? ExpiryDate);

public sealed class PurchaseInvoiceLine
{
    // Used by EF Core.
    private PurchaseInvoiceLine()
    {
    }

    internal PurchaseInvoiceLine(NewPurchaseLine line)
    {
        ProductId = line.ProductId;
        ProductUnitId = line.ProductUnitId;
        Quantity = line.Quantity;
        UnitFactor = line.UnitFactor;
        BaseQuantity = line.BaseQuantity;
        CostPerUnit = line.CostPerUnit;
        BaseUnitCost = line.BaseUnitCost;
        LineTotal = PurchaseInvoice.LineTotal(line.Quantity, line.CostPerUnit);
        BatchNumber = line.BatchNumber?.Trim() ?? string.Empty;
        ExpiryDate = line.ExpiryDate;
    }

    public int Id { get; private set; }

    public int InvoiceId { get; private set; }

    public int ProductId { get; private set; }

    public int ProductUnitId { get; private set; }

    /// <summary>Quantity in the chosen packaging unit.</summary>
    public int Quantity { get; private set; }

    /// <summary>How many base units one chosen unit holds, kept so the line stays readable if units change later.</summary>
    public int UnitFactor { get; private set; }

    public int BaseQuantity { get; private set; }

    /// <summary>Cost of one chosen unit, as written on the supplier invoice.</summary>
    public decimal CostPerUnit { get; private set; }

    /// <summary>Cost of one base unit; this is the cost stored on the batch.</summary>
    public decimal BaseUnitCost { get; private set; }

    public decimal LineTotal { get; private set; }

    /// <summary>The batch the goods went into; set once stock has been received.</summary>
    public int? BatchId { get; private set; }

    public string BatchNumber { get; private set; } = string.Empty;

    public DateOnly? ExpiryDate { get; private set; }

    internal void AssignBatch(int batchId, string batchNumber)
    {
        BatchId = batchId;
        BatchNumber = batchNumber;
    }
}

/// <summary>
/// A posted purchase: the goods received, how much of the price was paid in cash from the drawer, and how much
/// stays owed to the supplier. Invoices are never edited or deleted; a mistake is corrected with a purchase return.
/// </summary>
public sealed class PurchaseInvoice
{
    public const int NumberMaxLength = 30;
    public const int SupplierNameMaxLength = 150;
    public const int SupplierInvoiceNumberMaxLength = 50;
    public const int NoteMaxLength = 300;
    public const int MaxLines = 200;

    private readonly List<PurchaseInvoiceLine> _lines = [];

    // Used by EF Core.
    private PurchaseInvoice()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    /// <summary>The registered supplier; empty for a quick cash purchase from someone who is not registered.</summary>
    public int? SupplierId { get; private set; }

    /// <summary>Free-text name of an unregistered seller (only when there is no <see cref="SupplierId"/>).</summary>
    public string? SupplierName { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    /// <summary>The number printed on the supplier's own invoice, to find the paper again.</summary>
    public string? SupplierInvoiceNumber { get; private set; }

    public decimal Total { get; private set; }

    public decimal PaidCash { get; private set; }

    /// <summary>The part that stays owed to the supplier.</summary>
    public decimal OnCredit { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }

    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines;

    public static decimal LineTotal(int quantity, decimal costPerUnit) =>
        decimal.Round(quantity * costPerUnit, Supplier.Decimals, MidpointRounding.AwayFromZero);

    public static Result<PurchaseInvoice> Create(
        string number,
        int? supplierId,
        string? supplierName,
        DateOnly invoiceDate,
        DateOnly today,
        string? supplierInvoiceNumber,
        decimal paidCash,
        string? note,
        Guid operationId,
        DateTime createdAtUtc,
        int? userId,
        string userName,
        IReadOnlyList<NewPurchaseLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.NoLines));
        }

        if (lines.Count > MaxLines)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.TooManyLines, MaxLines));
        }

        if (invoiceDate > today)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.InvoiceDateInFuture));
        }

        var cleanSupplierName = Clean(supplierName);
        var cleanInvoiceNumber = Clean(supplierInvoiceNumber);
        var cleanNote = Clean(note);
        if (cleanSupplierName?.Length > SupplierNameMaxLength
            || cleanInvoiceNumber?.Length > SupplierInvoiceNumberMaxLength
            || cleanNote?.Length > NoteMaxLength)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.FieldTooLong));
        }

        var invoiceLines = lines.Select(line => new PurchaseInvoiceLine(line)).ToList();
        var total = invoiceLines.Sum(line => line.LineTotal);
        if (total > Supplier.MaxAmount)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.TotalTooLarge, Supplier.MaxAmount));
        }

        if (paidCash < 0 || paidCash > total || decimal.Round(paidCash, Supplier.Decimals) != paidCash)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.InvalidPaidAmount));
        }

        var onCredit = total - paidCash;
        if (onCredit > 0 && supplierId is null)
        {
            return Result.Failure<PurchaseInvoice>(new Error(PurchasingErrors.SupplierRequiredForCredit));
        }

        var invoice = new PurchaseInvoice
        {
            Number = number,
            SupplierId = supplierId,
            SupplierName = supplierId is null ? cleanSupplierName : null,
            InvoiceDate = invoiceDate,
            SupplierInvoiceNumber = cleanInvoiceNumber,
            Total = total,
            PaidCash = paidCash,
            OnCredit = onCredit,
            Note = cleanNote,
            CreatedAtUtc = createdAtUtc,
            UserId = userId,
            UserName = userName.Length <= SupplierAccountEntry.UserNameMaxLength ? userName : userName[..SupplierAccountEntry.UserNameMaxLength],
            OperationId = operationId,
        };
        invoice._lines.AddRange(invoiceLines);
        return Result.Success(invoice);
    }

    /// <summary>Records which batch a line's goods went into.</summary>
    public void AssignBatch(int lineIndex, Batch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _lines[lineIndex].AssignBatch(batch.Id, batch.BatchNumber);
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
