namespace Pharmacy.Domain.Expenses;

/// <summary>Expense error codes. Each code is a localization key in the UI resources.</summary>
public static class ExpensesErrors
{
    public const string UnknownCategory = "Expenses.UnknownCategory";
    public const string InactiveCategory = "Expenses.InactiveCategory";
    public const string InvalidAmount = "Expenses.InvalidAmount";
    public const string AmountTooLarge = "Expenses.AmountTooLarge";
    public const string DateInFuture = "Expenses.DateInFuture";
    public const string DescriptionRequired = "Expenses.DescriptionRequired";
    public const string FieldTooLong = "Expenses.FieldTooLong";
    public const string UnknownExpense = "Expenses.UnknownExpense";
    public const string AlreadyReversed = "Expenses.AlreadyReversed";
    public const string ReasonRequired = "Expenses.ReasonRequired";
}
