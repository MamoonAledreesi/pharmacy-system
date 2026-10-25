using Pharmacy.Domain.Catalog;

namespace Pharmacy.Domain.Expenses;

/// <summary>
/// A kind of expense (rent, electricity, wages, ...). A bilingual list like the catalog lists: categories are
/// deactivated rather than deleted because expenses reference them.
/// </summary>
public sealed class ExpenseCategory : NamedLookup;
