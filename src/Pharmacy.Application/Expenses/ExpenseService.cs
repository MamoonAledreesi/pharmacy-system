using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.Application.Expenses;

/// <summary>
/// Records expenses and reverses mistaken ones. Each runs in one transaction together with its cash movement and
/// audit entry: the money leaves the open shift when an expense is recorded and comes back when it is reversed.
/// </summary>
public sealed class ExpenseService(
    IPharmacyDbContext db, CashLedger cashLedger, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider clock)
{
    public const string ExpensePrefix = "EX";
    public const string ExpenseReference = "Expense";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Pays an expense from the open shift and returns its id. A repeated submission returns the first expense.</summary>
    public async Task<Result<int>> RecordAsync(RecordExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.ExpensesCreate))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var existing = await db.Expenses.AsNoTracking()
                .Where(e => e.OperationId == request.OperationId)
                .Select(e => (int?)e.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result.Success(existing.Value);
            }

            var category = await db.ExpenseCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
            if (category is null)
            {
                return Result.Failure<int>(new Error(ExpensesErrors.UnknownCategory));
            }

            if (!category.IsActive)
            {
                return Result.Failure<int>(new Error(ExpensesErrors.InactiveCategory));
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var localNow = clock.GetLocalNow();
            var number = await db.NextDocumentNumberAsync(ExpensePrefix, localNow.Year, cancellationToken);
            var created = Expense.Create(
                number, category.Id, request.Amount, request.ExpenseDate, DateOnly.FromDateTime(localNow.DateTime), request.Description,
                request.Payee, request.OperationId, now, currentUser.UserId, currentUser.UserName ?? string.Empty);
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            var expense = created.Value;
            db.Expenses.Add(expense);

            // The expense id is the reference of the cash movement, so save to get it.
            await db.SaveChangesAsync(cancellationToken);

            var cash = await cashLedger.PayAsync(
                CashMovementType.ExpensePayment, expense.Amount,
                new CashMovementInfo(expense.Number, ExpenseReference, expense.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }

            auditLog.Record(AuditActions.ExpenseRecorded, expense.Number, new
            {
                Category = category.NameEnglish,
                expense.Amount,
                expense.ExpenseDate,
                expense.Description,
                expense.Payee,
            });
            return Result.Success(expense.Id);
        }, Result.Failure<int>);
    }

    /// <summary>Takes back a mistaken expense: the cash returns to the open shift. Once per expense, with a reason.</summary>
    public async Task<Result> ReverseAsync(ReverseExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.ExpensesReverse))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var expense = await db.Expenses.SingleOrDefaultAsync(e => e.Id == request.ExpenseId, cancellationToken);
            if (expense is null)
            {
                return Result.Failure(new Error(ExpensesErrors.UnknownExpense));
            }

            if (expense.IsReversed)
            {
                return expense.ReverseOperationId == request.OperationId
                    ? Result.Success()
                    : Result.Failure(new Error(ExpensesErrors.AlreadyReversed));
            }

            var reversed = expense.Reverse(request.Reason, request.OperationId, clock.GetUtcNow().UtcDateTime, currentUser.UserName ?? string.Empty);
            if (reversed.IsFailure)
            {
                return reversed;
            }

            var cash = await cashLedger.ReceiveAsync(
                CashMovementType.ExpenseReversal, expense.Amount,
                new CashMovementInfo(expense.ReverseReason, ExpenseReference, expense.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure(cash.Error!);
            }

            auditLog.Record(AuditActions.ExpenseReversed, expense.Number, new { expense.Amount, Reason = expense.ReverseReason });
            return Result.Success();
        }, Result.Failure);
    }
}
