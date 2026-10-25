using Pharmacy.Application.Catalog;

namespace Pharmacy.Application.Expenses;

/// <summary>
/// An expense paid in cash from the open shift. <c>OperationId</c> is generated once per form so a double click or a
/// retry records the expense only once.
/// </summary>
public sealed record RecordExpenseRequest(
    Guid OperationId, int CategoryId, decimal Amount, DateOnly ExpenseDate, string? Description, string? Payee);

public sealed record ReverseExpenseRequest(Guid OperationId, int ExpenseId, string? Reason);

public sealed record ExpenseQuery(
    string? Text, int? CategoryId, DateOnly? From, DateOnly? To, bool IncludeReversed, int PageIndex, int PageSize);

public sealed record ExpenseListItem(
    int Id,
    string Number,
    DateOnly ExpenseDate,
    LocalizedName Category,
    decimal Amount,
    string Description,
    string? Payee,
    string UserName,
    bool IsReversed,
    string? ReverseReason,
    string? ReversedByUserName);

public sealed record ExpenseCategoryTotal(int CategoryId, LocalizedName Category, decimal Total, int Count);

/// <summary>What was spent in a period. Reversed expenses are not counted.</summary>
public sealed record ExpenseTotals(decimal Total, int Count, IReadOnlyList<ExpenseCategoryTotal> ByCategory);
