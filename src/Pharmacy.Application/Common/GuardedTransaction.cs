using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Common;

public static class GuardedTransaction
{
    /// <summary>
    /// Runs the work in a database transaction (see <see cref="IPharmacyDbContext.ExecuteInTransactionAsync{TResult}"/>).
    /// When two people change the same thing at the same moment the database refuses one of them (a stale row version
    /// or a unique index). Nothing was saved in that case, so it is reported as a conflict the user can simply retry.
    /// </summary>
    public static async Task<TResult> RunGuardedAsync<TResult>(
        this IPharmacyDbContext db, Func<Task<TResult>> work, Func<Error, TResult> failure)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(failure);

        try
        {
            return await db.ExecuteInTransactionAsync(work);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or DbUpdateException)
        {
            return failure(new Error(CommonErrors.ConcurrencyConflict));
        }
    }
}
