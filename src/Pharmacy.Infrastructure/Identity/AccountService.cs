using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Users;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.Infrastructure.Identity;

internal sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    PharmacyDbContext db,
    IAuditLog auditLog,
    TimeProvider clock) : IAccountService
{
    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) =>
        !await db.Users.AnyAsync(cancellationToken);

    public async Task<Result> CompleteSetupAsync(
        string userName, string fullName, string password, CancellationToken cancellationToken = default)
    {
        var trimmedFullName = fullName?.Trim() ?? string.Empty;
        if (trimmedFullName.Length == 0)
        {
            return Result.Failure(new Error(UserErrors.FullNameRequired));
        }

        if (trimmedFullName.Length > UserRules.FullNameMaxLength)
        {
            return Result.Failure(new Error(UserErrors.FullNameTooLong, UserRules.FullNameMaxLength));
        }

        return await db.InTransactionAsync(async () =>
        {
            // An application lock serializes concurrent setup attempts, so exactly one administrator
            // can be created even if two browsers submit the setup form at the same moment.
            await db.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = N'PharmacySetup', @LockMode = N'Exclusive', @LockOwner = N'Transaction'",
                cancellationToken);

            if (await db.Users.AnyAsync(cancellationToken))
            {
                return Result.Failure(new Error(UserErrors.SetupAlreadyCompleted));
            }

            var administrator = new ApplicationUser
            {
                UserName = userName?.Trim(),
                FullName = trimmedFullName,
                IsActive = true,
                MustChangePassword = false,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            };

            var created = (await userManager.CreateAsync(administrator, password)).ToResult();
            if (created.IsFailure)
            {
                return created;
            }

            var roleAdded = (await userManager.AddToRoleAsync(administrator, SystemRoles.Administrator)).ToResult();
            if (roleAdded.IsFailure)
            {
                return roleAdded;
            }

            auditLog.RecordFor(administrator.Id, administrator.UserName!, AuditActions.SetupCompleted, administrator.UserName);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> ChangePasswordAsync(
        int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        return await db.InTransactionAsync(async () =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
            if (user is null || !user.IsActive)
            {
                return Result.Failure(new Error(CommonErrors.NotFound));
            }

            var changed = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!changed.Succeeded)
            {
                return changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                    ? Result.Failure(new Error(UserErrors.CurrentPasswordIncorrect))
                    : changed.ToResult();
            }

            user.MustChangePassword = false;
            var updated = (await userManager.UpdateAsync(user)).ToResult();
            if (updated.IsFailure)
            {
                return updated;
            }

            auditLog.RecordFor(user.Id, user.UserName!, AuditActions.PasswordChanged, user.UserName);
            return Result.Success();
        }, cancellationToken);
    }
}
