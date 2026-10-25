using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Purchasing;

/// <summary>
/// Cash paid to a supplier from the open shift. A payment entered by mistake is not deleted: it is reversed
/// (once), which puts the cash back in the drawer and the amount back on the supplier balance.
/// </summary>
public sealed class SupplierPayment
{
    // Used by EF Core.
    private SupplierPayment()
    {
    }

    public int Id { get; private set; }

    public int SupplierId { get; private set; }

    public decimal Amount { get; private set; }

    public DateTime PaidAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public Guid OperationId { get; private set; }

    public bool IsReversed { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }

    public string? ReversedByUserName { get; private set; }

    public string? ReverseReason { get; private set; }

    public Guid? ReverseOperationId { get; private set; }

    public static Result<SupplierPayment> Create(
        int supplierId, decimal amount, string? note, Guid operationId, DateTime paidAtUtc, int? userId, string userName)
    {
        if (!Supplier.IsValidAmount(amount))
        {
            return Result.Failure<SupplierPayment>(new Error(PurchasingErrors.InvalidAmount));
        }

        var cleanNote = note?.Trim();
        if (cleanNote?.Length > SupplierAccountEntry.NoteMaxLength)
        {
            return Result.Failure<SupplierPayment>(new Error(PurchasingErrors.NoteTooLong, SupplierAccountEntry.NoteMaxLength));
        }

        return Result.Success(new SupplierPayment
        {
            SupplierId = supplierId,
            Amount = amount,
            PaidAtUtc = paidAtUtc,
            UserId = userId,
            UserName = Truncate(userName),
            Note = string.IsNullOrEmpty(cleanNote) ? null : cleanNote,
            OperationId = operationId,
        });
    }

    public Result Reverse(string? reason, Guid operationId, DateTime reversedAtUtc, string userName)
    {
        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason))
        {
            return Result.Failure(new Error(PurchasingErrors.ReasonRequired));
        }

        if (cleanReason.Length > SupplierAccountEntry.NoteMaxLength)
        {
            return Result.Failure(new Error(PurchasingErrors.NoteTooLong, SupplierAccountEntry.NoteMaxLength));
        }

        if (IsReversed)
        {
            return Result.Failure(new Error(PurchasingErrors.PaymentAlreadyReversed));
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
