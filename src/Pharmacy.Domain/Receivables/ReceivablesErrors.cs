namespace Pharmacy.Domain.Receivables;

/// <summary>Customers and receivables error codes. Each code is a localization key in the UI resources.</summary>
public static class ReceivablesErrors
{
    public const string UnknownCustomer = "Receivables.UnknownCustomer";
    public const string InactiveCustomer = "Receivables.InactiveCustomer";
    public const string CustomerNameRequired = "Receivables.CustomerNameRequired";
    public const string CustomerNameTooLong = "Receivables.CustomerNameTooLong";
    public const string FieldTooLong = "Receivables.FieldTooLong";
    public const string DefaultCustomerCannotBeDeactivated = "Receivables.DefaultCustomerCannotBeDeactivated";

    public const string InvalidAmount = "Receivables.InvalidAmount";
    public const string AmountTooLarge = "Receivables.AmountTooLarge";

    /// <summary>The collection is more than the customer owes. Argument 0 is the amount owed.</summary>
    public const string CollectionExceedsBalance = "Receivables.CollectionExceedsBalance";

    public const string UnknownCollection = "Receivables.UnknownCollection";
    public const string CollectionAlreadyReversed = "Receivables.CollectionAlreadyReversed";

    public const string ReasonRequired = "Receivables.ReasonRequired";
    public const string NoteTooLong ="Receivables.NoteTooLong";
    public const string OpeningBalanceAlreadySet = "Receivables.OpeningBalanceAlreadySet";
}
