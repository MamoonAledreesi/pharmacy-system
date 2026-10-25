namespace Pharmacy.Domain.Inventory;

/// <summary>
/// Reason codes for manual stock operations. "StockReasons.{code}" is the localization key of each label.
/// <see cref="Other"/> requires a free-text note.
/// </summary>
public static class StockReasons
{
    public const string Damaged = "Damaged";
    public const string Lost = "Lost";
    public const string CountDifference = "CountDifference";
    public const string Expired = "Expired";
    public const string Other = "Other";

    public const int NoteMaxLength = 300;

    public static IReadOnlyList<string> ForAdjustment { get; } = [CountDifference, Damaged, Lost, Other];

    public static IReadOnlyList<string> ForQuarantine { get; } = [Damaged, Other];

    public static IReadOnlyList<string> ForWriteOff { get; } = [Expired, Damaged, Lost, Other];

    public static IReadOnlyList<string> All { get; } = [CountDifference, Damaged, Lost, Expired, Other];
}
