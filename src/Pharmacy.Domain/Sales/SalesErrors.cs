namespace Pharmacy.Domain.Sales;

/// <summary>Sales error codes. Each code is a localization key in the UI resources.</summary>
public static class SalesErrors
{
    public const string NoLines = "Sales.NoLines";
    public const string TooManyLines = "Sales.TooManyLines";
    public const string InvalidPrice = "Sales.InvalidPrice";
    public const string TotalTooLarge = "Sales.TotalTooLarge";

    public const string InvalidDiscount = "Sales.InvalidDiscount";
    public const string DiscountTooLarge = "Sales.DiscountTooLarge";
    public const string InvalidPaidAmount = "Sales.InvalidPaidAmount";
    public const string FieldTooLong = "Sales.FieldTooLong";

    public const string UnknownCustomer = "Sales.UnknownCustomer";
    public const string InactiveCustomer = "Sales.InactiveCustomer";

    /// <summary>Credit needs a named customer, not the default cash customer.</summary>
    public const string CreditNeedsRegisteredCustomer = "Sales.CreditNeedsRegisteredCustomer";

    /// <summary>Some lines sell below cost and the user may not do that alone. The page asks for a supervisor.</summary>
    public const string BelowCostNeedsApproval = "Sales.BelowCostNeedsApproval";

    /// <summary>The discount is above the allowed percentage and the user may not give it alone. Argument 0 is the limit.</summary>
    public const string HighDiscountNeedsApproval = "Sales.HighDiscountNeedsApproval";

    public const string BelowCostReasonRequired = "Sales.BelowCostReasonRequired";

    /// <summary>Wrong user name, wrong password, locked or inactive account, or the user lacks the permission.</summary>
    public const string SupervisorNotAccepted = "Sales.SupervisorNotAccepted";

    public const string UnknownInvoice = "Sales.UnknownInvoice";
}
