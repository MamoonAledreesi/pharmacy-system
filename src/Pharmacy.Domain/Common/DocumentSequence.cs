namespace Pharmacy.Domain.Common;

/// <summary>
/// The last number handed out for one kind of document in one year ("PI" 2026 gives PI-2026-000001, ...).
/// It is updated inside the transaction of the document that uses it, so numbers have no gaps.
/// </summary>
public sealed class DocumentSequence
{
    public const int PrefixMaxLength = 10;

    // Used by EF Core.
    private DocumentSequence()
    {
    }

    public DocumentSequence(string prefix, int year)
    {
        Prefix = prefix;
        Year = year;
    }

    public int Id { get; private set; }

    public string Prefix { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public int LastNumber { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Takes the next number and returns the formatted document number.</summary>
    public string Next()
    {
        LastNumber++;
        return $"{Prefix}-{Year}-{LastNumber:D6}";
    }
}
