using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Domain.Expenses;

/// <summary>
/// Money spent from the cash drawer on running the pharmacy. An expense is never edited or deleted: a mistake is
/// corrected by reversing it (once, with a reason), which puts the cash back, and entering a correct one. A reversed
/// expense stays visible but no longer counts in totals.
/// </summary>
public sealed class Expense
{
    public const int NumberMaxLength = 30;
    public const int DescriptionMaxLength = 300;
    public const int PayeeMaxLength = 150;
    public const int ReasonMaxLength = 300;
    public const decimal MaxAmount = 100_000_000m;
    public const int Decimals = 2;

    // Used by EF Core.
    private Expense()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public int CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>The day the money was spent on. Profit reports count the expense on this day.</summary>
    public DateOnly ExpenseDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    /// <summary>Who was paid (landlord, electricity company, an employee, ...), when worth noting.</summary>
    public string? Payee { get; private set; }

    /// <summary>When it was entered; the cash left the drawer at that moment.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }

    public bool IsReversed { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }

    public string? ReversedByUserName { get; private set; }

    public string? ReverseReason { get; private set; }

    public Guid? ReverseOperationId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static bool IsValidAmount(decimal amount) =>
        amount is > 0 and <= MaxAmount && decimal.Round(amount, Decimals) == amount;

    public static Result<Expense> Create(
        string number,
        int categoryId,
        decimal amount,
        DateOnly expenseDate,
        DateOnly today,
        string? description,
        string? payee,
        Guid operationId,
        DateTime createdAtUtc,
        int? userId,
        string userName)
    {
        if (!IsValidAmount(amount))
        {
            return Result.Failure<Expense>(new Error(amount > MaxAmount ? ExpensesErrors.AmountTooLarge : ExpensesErrors.InvalidAmount, MaxAmount));
        }

        if (expenseDate > today)
        {
            return Result.Failure<Expense>(new Error(ExpensesErrors.DateInFuture));
        }

        var cleanDescription = description?.Trim();
        if (string.IsNullOrEmpty(cleanDescription))
        {
            return Result.Failure<Expense>(new Error(ExpensesErrors.DescriptionRequired));
        }

        var cleanPayee = payee?.Trim();
        if (cleanDescription.Length > DescriptionMaxLength || cleanPayee?.Length > PayeeMaxLength)
        {
            return Result.Failure<Expense>(new Error(ExpensesErrors.FieldTooLong));
        }

        return Result.Success(new Expense
        {
            Number = number,
            CategoryId = categoryId,
            Amount = amount,
            ExpenseDate = expenseDate,
            Description = cleanDescription,
            Payee = string.IsNullOrEmpty(cleanPayee) ? null : cleanPayee,
            CreatedAtUtc = createdAtUtc,
            UserId = userId,
            UserName = Truncate(userName),
            OperationId = operationId,
        });
    }

    public Result Reverse(string? reason, Guid operationId, DateTime reversedAtUtc, string userName)
    {
        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason))
        {
            return Result.Failure(new Error(ExpensesErrors.ReasonRequired));
        }

        if (cleanReason.Length > ReasonMaxLength)
        {
            return Result.Failure(new Error(ExpensesErrors.FieldTooLong));
        }

        if (IsReversed)
        {
            return Result.Failure(new Error(ExpensesErrors.AlreadyReversed));
        }

        IsReversed = true;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserName = Truncate(userName);
        ReverseReason = cleanReason;
        ReverseOperationId = operationId;
        return Result.Success();
    }

    private static string Truncate(string userName) =>
        userName.Length <= SupplierAccountEntry.UserNameMaxLength ? userName : userName[..SupplierAccountEntry.UserNameMaxLength];
}
