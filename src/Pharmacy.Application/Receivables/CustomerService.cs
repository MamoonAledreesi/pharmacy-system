using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Application.Receivables;

/// <summary>
/// Creates and edits customers. Requires Customers.Manage. Customers are deactivated, never deleted. Names may
/// repeat (two people can share a name); a repeated phone number is reported by the query service so the page can warn.
/// </summary>
public sealed class CustomerService(IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog)
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<int>> CreateAsync(CustomerInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!currentUser.HasPermission(Permissions.CustomersManage))
        {
            return Result.Failure<int>(_forbidden);
        }

        var created = Customer.Create(input.Name, input.Phone, input.Address, input.Notes);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var customer = created.Value;
        return await db.RunGuardedAsync(async () =>
        {
            db.Customers.Add(customer);
            auditLog.Record(AuditActions.CustomerCreated, customer.Name, new { customer.Phone });

            // The id is assigned by the database, so save before returning it.
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(customer.Id);
        }, Result.Failure<int>);
    }

    public async Task<Result> UpdateAsync(int id, CustomerInput input, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (!currentUser.HasPermission(Permissions.CustomersManage))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (customer is null)
            {
                return Result.Failure(new Error(ReceivablesErrors.UnknownCustomer));
            }

            var before = new { customer.Name, customer.Phone, customer.Address, customer.Notes, customer.IsActive };
            db.Entry(customer).Property(c => c.RowVersion).OriginalValue = rowVersion;

            var updated = customer.Update(input.Name, input.Phone, input.Address, input.Notes, input.IsActive);
            if (updated.IsFailure)
            {
                return updated;
            }

            auditLog.Record(AuditActions.CustomerUpdated, customer.Name, new
            {
                Before = before,
                After = new { customer.Name, customer.Phone, customer.Address, customer.Notes, customer.IsActive },
            });
            return Result.Success();
        }, Result.Failure);
    }
}
