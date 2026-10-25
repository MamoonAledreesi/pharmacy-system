namespace Pharmacy.Domain.Cash;

/// <summary>Cash register error codes. Each code is a localization key in the UI resources.</summary>
public static class CashErrors
{
    public const string NoOpenShift = "Cash.NoOpenShift";
    public const string ShiftAlreadyOpen = "Cash.ShiftAlreadyOpen";
    public const string ShiftClosed = "Cash.ShiftClosed";
    public const string UnknownShift = "Cash.UnknownShift";

    public const string InvalidAmount = "Cash.InvalidAmount";
    public const string AmountTooLarge = "Cash.AmountTooLarge";

    /// <summary>The drawer holds less than the payment. Argument 0 is the amount in the drawer.</summary>
    public const string InsufficientCash = "Cash.InsufficientCash";

    public const string NoteRequired = "Cash.NoteRequired";
    public const string NoteTooLong = "Cash.NoteTooLong";

    /// <summary>The opening balance differs from what the last shift counted, so the user must say why.</summary>
    public const string OpeningDifferenceNoteRequired = "Cash.OpeningDifferenceNoteRequired";

    /// <summary>The counted cash differs from the expected cash, so the user must say why.</summary>
    public const string CloseDifferenceNoteRequired = "Cash.CloseDifferenceNoteRequired";

    /// <summary>Closing someone else's shift needs a reason.</summary>
    public const string ForceCloseReasonRequired = "Cash.ForceCloseReasonRequired";
}
