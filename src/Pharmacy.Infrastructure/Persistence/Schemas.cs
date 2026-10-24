namespace Pharmacy.Infrastructure.Persistence;

/// <summary>One SQL Server schema per module.</summary>
internal static class Schemas
{
    public const string Config = "config";
    public const string Identity = "identity";
    public const string Audit = "audit";
    public const string Catalog = "catalog";
}
