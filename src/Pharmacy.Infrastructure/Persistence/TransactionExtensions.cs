using System.Data;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Domain.Common;

namespace Pharmacy.Infrastructure.Persistence;

internal static class TransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in a database transaction. On success, pending changes (such as audit
    /// entries) are saved and committed; on a business failure everything is rolled back.
    /// ASP.NET Identity managers save on their own, so they must share this context and run inside it.
    /// </summary>
    public static async Task<TResult> InTransactionAsync<TResult>(
        this PharmacyDbContext db,
        Func<Task<TResult>> work,
        CancellationToken cancellationToken,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        where TResult : Result
    {
        await using var transaction = await db.Database.BeginTransactionAsync(isolationLevel, cancellationToken);

        var result = await work();
        if (result.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
