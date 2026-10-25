using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

/// <summary>
/// What the pharmacy owes its suppliers: the opening balance, payments from the open shift, and reversing a payment
/// entered by mistake. Each runs in one transaction together with its cash movement, statement entry, and audit entry.
/// </summary>
public sealed class PayablesService(
    IPharmacyDbContext db, CashLedger cashLedger, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider clock)
{
    public const string PaymentReference = "SupplierPayment";

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Pays part or all of what is owed, in cash from the open shift. Never more than the balance.</summary>
    public async Task<Result<int>> PayAsync(PaySupplierRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.PayablesPay))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var existing = await db.SupplierPayments.AsNoTracking()
                .Where(p => p.OperationId == request.OperationId)
                .Select(p => (int?)p.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result.Success(existing.Value);
            }

            var supplier = await db.LoadSupplierForUpdateAsync(request.SupplierId, cancellationToken);
            if (supplier is null)
            {
                return Result.Failure<int>(new Error(PurchasingErrors.UnknownSupplier));
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var userName = currentUser.UserName ?? string.Empty;
            var created = SupplierPayment.Create(supplier.Id, request.Amount, request.Note, request.OperationId, now, currentUser.UserId, userName);
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            var payment = created.Value;
            db.SupplierPayments.Add(payment);

            // The payment id is the reference of the cash movement and the statement entry, so save to get it.
            await db.SaveChangesAsync(cancellationToken);

            // The balance check comes first so "more than owed" is reported before "not enough cash".
            var entry = supplier.Decrease(
                SupplierEntryType.Payment, payment.Amount,
                new AccountContext(now, currentUser.UserId, userName, payment.Note, PaymentReference, payment.Id));
            if (entry.IsFailure)
            {
                return Result.Failure<int>(entry.Error!);
            }

            var cash = await cashLedger.PayAsync(
                CashMovementType.SupplierPayment, payment.Amount,
                new CashMovementInfo(payment.Note, PaymentReference, payment.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure<int>(cash.Error!);
            }

            db.SupplierAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.SupplierPaid, supplier.Name, new { payment.Amount, BalanceAfter = supplier.CurrentBalance, payment.Note });
            return Result.Success(payment.Id);
        }, Result.Failure<int>);
    }

    /// <summary>Takes back a payment entered by mistake: the cash returns to the open shift and the debt returns to the supplier.</summary>
    public async Task<Result> ReversePaymentAsync(ReversePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.PayablesReverse))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var payment = await db.SupplierPayments.SingleOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);
            if (payment is null)
            {
                return Result.Failure(new Error(PurchasingErrors.UnknownPayment));
            }

            if (payment.IsReversed)
            {
                return payment.ReverseOperationId == request.OperationId
                    ? Result.Success()
                    : Result.Failure(new Error(PurchasingErrors.PaymentAlreadyReversed));
            }

            var supplier = await db.LoadSupplierForUpdateAsync(payment.SupplierId, cancellationToken);
            if (supplier is null)
            {
                return Result.Failure(new Error(PurchasingErrors.UnknownSupplier));
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var userName = currentUser.UserName ?? string.Empty;
            var reversed = payment.Reverse(request.Reason, request.OperationId, now, userName);
            if (reversed.IsFailure)
            {
                return reversed;
            }

            var cash = await cashLedger.ReceiveAsync(
                CashMovementType.SupplierPaymentReversal, payment.Amount,
                new CashMovementInfo(payment.ReverseReason, PaymentReference, payment.Id), cancellationToken);
            if (cash.IsFailure)
            {
                return Result.Failure(cash.Error!);
            }

            var entry = supplier.Increase(
                SupplierEntryType.PaymentReversal, payment.Amount,
                new AccountContext(now, currentUser.UserId, userName, payment.ReverseReason, PaymentReference, payment.Id));
            if (entry.IsFailure)
            {
                return Result.Failure(entry.Error!);
            }

            db.SupplierAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.SupplierPaymentReversed, supplier.Name, new
            {
                payment.Amount,
                payment.PaidAtUtc,
                Reason = payment.ReverseReason,
                BalanceAfter = supplier.CurrentBalance,
            });
            return Result.Success();
        }, Result.Failure);
    }

    /// <summary>Records what the pharmacy already owed a supplier when the system started. Allowed once per supplier.</summary>
    public async Task<Result> SetOpeningBalanceAsync(OpeningBalanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.PayablesSetOpeningBalance))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            if (await db.SupplierAccountEntries.AnyAsync(e => e.OperationId == request.OperationId, cancellationToken))
            {
                return Result.Success();
            }

            var supplier = await db.LoadSupplierForUpdateAsync(request.SupplierId, cancellationToken);
            if (supplier is null)
            {
                return Result.Failure(new Error(PurchasingErrors.UnknownSupplier));
            }

            if (await db.SupplierAccountEntries.AnyAsync(
                    e => e.SupplierId == supplier.Id && e.Type == SupplierEntryType.OpeningBalance, cancellationToken))
            {
                return Result.Failure(new Error(PurchasingErrors.OpeningBalanceAlreadySet));
            }

            var note = request.Note?.Trim();
            if (note?.Length > SupplierAccountEntry.NoteMaxLength)
            {
                return Result.Failure(new Error(PurchasingErrors.NoteTooLong, SupplierAccountEntry.NoteMaxLength));
            }

            var entry = supplier.Increase(
                SupplierEntryType.OpeningBalance, request.Amount,
                new AccountContext(
                    clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.UserName ?? string.Empty,
                    string.IsNullOrEmpty(note) ? null : note, OperationId: request.OperationId));
            if (entry.IsFailure)
            {
                return Result.Failure(entry.Error!);
            }

            db.SupplierAccountEntries.Add(entry.Value);
            auditLog.Record(AuditActions.SupplierOpeningBalance, supplier.Name, new { request.Amount, Note = note });
            return Result.Success();
        }, Result.Failure);
    }
}
