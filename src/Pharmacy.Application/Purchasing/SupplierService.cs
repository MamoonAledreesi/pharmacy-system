using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Application.Purchasing;

/// <summary>Creates and edits suppliers. Requires Suppliers.Manage. Suppliers are deactivated, never deleted.</summary>
public sealed class SupplierService(IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog)
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<int>> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!currentUser.HasPermission(Permissions.SuppliersManage))
        {
            return Result.Failure<int>(_forbidden);
        }

        var created = Supplier.Create(input.Name, input.Phone, input.Address, input.Notes);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var supplier = created.Value;
        return await db.RunGuardedAsync(async () =>
        {
            if (await NameTakenAsync(supplier.Name, exceptId: null, cancellationToken))
            {
                return Result.Failure<int>(new Error(PurchasingErrors.DuplicateSupplierName));
            }

            db.Suppliers.Add(supplier);
            auditLog.Record(AuditActions.SupplierCreated, supplier.Name, new { supplier.Phone });

            // The id is assigned by the database, so save before returning it.
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(supplier.Id);
        }, Result.Failure<int>);
    }

    public async Task<Result> UpdateAsync(int id, SupplierInput input, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (!currentUser.HasPermission(Permissions.SuppliersManage))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (supplier is null)
            {
                return Result.Failure(new Error(PurchasingErrors.UnknownSupplier));
            }

            var before = new { supplier.Name, supplier.Phone, supplier.Address, supplier.Notes, supplier.IsActive };
            db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = rowVersion;

            var updated = supplier.Update(input.Name, input.Phone, input.Address, input.Notes, input.IsActive);
            if (updated.IsFailure)
            {
                return updated;
            }

            if (await NameTakenAsync(supplier.Name, exceptId: id, cancellationToken))
            {
                return Result.Failure(new Error(PurchasingErrors.DuplicateSupplierName));
            }

            auditLog.Record(AuditActions.SupplierUpdated, supplier.Name, new
            {
                Before = before,
                After = new { supplier.Name, supplier.Phone, supplier.Address, supplier.Notes, supplier.IsActive },
            });
            return Result.Success();
        }, Result.Failure);
    }

    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken cancellationToken) =>
        db.Suppliers.AnyAsync(s => s.Name == name && s.Id != exceptId, cancellationToken);
}
