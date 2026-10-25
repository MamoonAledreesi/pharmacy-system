using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Application.Receivables;

/// <summary>
/// What customers owe the pharmacy: the opening balance, collecting cash into the open shift, and reversing a
/// collection entered by mistake. Each runs in one transaction together with its cash movement, statement entry,
/// and audit entry. Only a collection lowers a customer's debt: paying cash for a new sale does not.
/// </summary>
public sealed class ReceivablesService(
    IPharmacyDbContext db, CashLedger cashLedger, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider clock)
{
    public const string CollectionPrefix = "RC";
    public const string CollectionReference = "DebtCollection";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Collects part or all of what the customer owes, in cash into the open shift. Never more than the balance.</summary>
    public async Task<Result<int>> CollectAsync(CollectDebtRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.ReceivablesCollect))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var existing = await db.DebtCollections.AsNoTracking()
                .Where(c => c.OperationId == request.OperationId)
                .Select(c => (int?)c.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result.Success(existing.Value);
            }

            var customer = await db.LoadCustomerForUpdateAsync(request.CustomerId, cancellationToken);
            if (customer is null)
            {
                return Result.Failure<int>(new Error(ReceivablesErrors.UnknownCustomer));
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var userName = currentUser.UserName ?? string.Empty;
            var number = await db.NextDocumentNumberAsync(CollectionPrefix, DateOnly.FromDateTime(clock.GetLocalNow().DateTime).Year, cancellationToken);
            var created = DebtCollection.Create(number, customer.Id, request.Amount, request.Note, request.OperationId, now, currentUser.UserId, userName);
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            var collection = created.Value;
            db.DebtCollections.Add(collection);

            // The collection id is the reference of the cash movement and the statement entry, so save to get it.
            await db.SaveChangesAsync(cancellationToken);

            // The balance check comes first so "more than owed" is reported before "no open shift".
            var entry = customer.Credit(
                CustomerEntryType.Collection, collection.Amount,
                new CustomerAccountContext(now, currentUser.UserId, userName, collection.Note, CollectionReference, collection.Id));
            if (entry.IsFailure)
            {
                return Result.Failure<int>(entry.Error!);
            }

            var cash = await cashLedger.ReceiveAsync(
                CashMovementType.DebtCollection, collection.Amount,
                new CashMovementInfo(collection.Number, CollectionReference, collection.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }

            db.CustomerAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.DebtCollected, customer.Name, new
            {
                collection.Number,
                collection.Amount,
                BalanceAfter = customer.CurrentBalance,
                collection.Note,
            });
            return Result.Success(collection.Id);
        }, Result.Failure<int>);
    }

    /// <summary>Takes back a collection entered by mistake: the cash leaves the open shift and the debt returns to the customer.</summary>
    public async Task<Result> ReverseCollectionAsync(ReverseCollectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.ReceivablesReverse))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var collection = await db.DebtCollections.SingleOrDefaultAsync(c => c.Id == request.CollectionId, cancellationToken);
            if (collection is null)
            {
                return Result.Failure(new Error(ReceivablesErrors.UnknownCollection));
            }

            if (collection.IsReversed)
            {
                return collection.ReverseOperationId == request.OperationId
                    ? Result.Success()
                    : Result.Failure(new Error(ReceivablesErrors.CollectionAlreadyReversed));
            }

            var customer = await db.LoadCustomerForUpdateAsync(collection.CustomerId, cancellationToken);
            if (customer is null)
            {
                return Result.Failure(new Error(ReceivablesErrors.UnknownCustomer));
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var userName = currentUser.UserName ?? string.Empty;
            var reversed = collection.Reverse(request.Reason, request.OperationId, now, userName);
            if (reversed.IsFailure)
            {
                return reversed;
            }

            var cash = await cashLedger.PayAsync(
                CashMovementType.DebtCollectionReversal, collection.Amount,
                new CashMovementInfo(collection.ReverseReason, CollectionReference, collection.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure(cash.Error!);
            }

            var entry = customer.Debit(
                CustomerEntryType.CollectionReversal, collection.Amount,
                new CustomerAccountContext(now, currentUser.UserId, userName, collection.ReverseReason, CollectionReference, collection.Id));
            if (entry.IsFailure)
            {
                return Result.Failure(entry.Error!);
            }

            db.CustomerAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.CollectionReversed, customer.Name, new
            {
                collection.Number,
                collection.Amount,
                collection.CollectedAtUtc,
                Reason = collection.ReverseReason,
                BalanceAfter = customer.CurrentBalance,
            });
            return Result.Success();
        }, Result.Failure);
    }

    /// <summary>Records what a customer already owed when the system started. Allowed once per customer.</summary>
    public async Task<Result> SetOpeningBalanceAsync(CustomerOpeningBalanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.ReceivablesSetOpeningBalance))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            if (await db.CustomerAccountEntries.AnyAsync(e => e.OperationId == request.OperationId, cancellationToken))
            {
                return Result.Success();
            }

            var customer = await db.LoadCustomerForUpdateAsync(request.CustomerId, cancellationToken);
            if (customer is null)
            {
                return Result.Failure(new Error(ReceivablesErrors.UnknownCustomer));
            }

            if (await db.CustomerAccountEntries.AnyAsync(
                    e => e.CustomerId == customer.Id && e.Type == CustomerEntryType.OpeningBalance, cancellationToken))
            {
                return Result.Failure(new Error(ReceivablesErrors.OpeningBalanceAlreadySet));
            }

            var note = request.Note?.Trim();
            if (note?.Length > CustomerAccountEntry.NoteMaxLength)
            {
                return Result.Failure(new Error(ReceivablesErrors.NoteTooLong, CustomerAccountEntry.NoteMaxLength));
            }

            var entry = customer.Debit(
                CustomerEntryType.OpeningBalance, request.Amount,
                new CustomerAccountContext(
                    clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.UserName ?? string.Empty,
                    string.IsNullOrEmpty(note) ? null : note, OperationId: request.OperationId));
            if (entry.IsFailure)
            {
                return Result.Failure(entry.Error!);
            }

            db.CustomerAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.CustomerOpeningBalance, customer.Name, new { request.Amount, Note = note });
            return Result.Success();
        }, Result.Failure);
    }
}
