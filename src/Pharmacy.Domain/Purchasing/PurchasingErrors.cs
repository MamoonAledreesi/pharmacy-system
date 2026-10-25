namespace Pharmacy.Domain.Purchasing;

/// <summary>Suppliers, purchases, and supplier payables error codes. Each code is a localization key in the UI resources.</summary>
public static class PurchasingErrors
{
    public const string UnknownSupplier = "Purchasing.UnknownSupplier";
    public const string InactiveSupplier = "Purchasing.InactiveSupplier";
    public const string SupplierNameRequired = "Purchasing.SupplierNameRequired";
    public const string SupplierNameTooLong = "Purchasing.SupplierNameTooLong";
    public const string DuplicateSupplierName = "Purchasing.DuplicateSupplierName";
    public const string FieldTooLong = "Purchasing.FieldTooLong";

    public const string NoLines = "Purchasing.NoLines";
    public const string TooManyLines = "Purchasing.TooManyLines";
    public const string InvoiceDateInFuture = "Purchasing.InvoiceDateInFuture";
    public const string InvalidPaidAmount = "Purchasing.InvalidPaidAmount";
    public const string SupplierRequiredForCredit = "Purchasing.SupplierRequiredForCredit";
    public const string TotalTooLarge = "Purchasing.TotalTooLarge";
    public const string ExpiryInPast = "Purchasing.ExpiryInPast";

    public const string InvalidAmount = "Purchasing.InvalidAmount";

    /// <summary>The payment is more than the supplier is owed. Argument 0 is the amount owed.</summary>
    public const string PaymentExceedsBalance = "Purchasing.PaymentExceedsBalance";

    public const string UnknownPayment = "Purchasing.UnknownPayment";
    public const string PaymentAlreadyReversed = "Purchasing.PaymentAlreadyReversed";
    public const string ReasonRequired = "Purchasing.ReasonRequired";
    public const string NoteTooLong = "Purchasing.NoteTooLong";
    public const string OpeningBalanceAlreadySet = "Purchasing.OpeningBalanceAlreadySet";

    public const string ReturnNoLines = "Purchasing.ReturnNoLines";
    public const string UnknownInvoice = "Purchasing.UnknownInvoice";
    public const string UnknownInvoiceLine = "Purchasing.UnknownInvoiceLine";
    public const string DuplicateReturnLine = "Purchasing.DuplicateReturnLine";
    public const string ReturnReasonRequired = "Purchasing.ReturnReasonRequired";
    public const string InvalidReturnAmount = "Purchasing.InvalidReturnAmount";

    /// <summary>More was asked back than was received and not yet returned. Argument 0 is what is left (base units).</summary>
    public const string ReturnQuantityExceeds = "Purchasing.ReturnQuantityExceeds";

    /// <summary>The batch no longer holds that much, because some was sold. Argument 0 is what the batch holds (base units).</summary>
    public const string BatchStockTooLow = "Purchasing.BatchStockTooLow";
}
