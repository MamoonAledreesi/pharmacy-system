using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Users;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.Infrastructure.Identity;

internal sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    PharmacyDbContext db,
    IAuditLog auditLog,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserManagementService
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);
    private static readonly Error _notFound = new(CommonErrors.NotFound);

    public async Task<Result<IReadOnlyList<UserSummary>>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            return Result.Failure<IReadOnlyList<UserSummary>>(_forbidden);
        }

        var roleNames = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name!, cancellationToken);
        var rolesByUser = (await db.UserRoles.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(ur => ur.UserId, ur => roleNames[ur.RoleId]);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        IReadOnlyList<UserSummary> summaries =
        [
            .. users.Select(u => new UserSummary(
                u.Id,
                u.UserName!,
                u.FullName,
                u.IsActive,
                u.LockoutEnd > now,
                [.. rolesByUser[u.Id].Order(StringComparer.Ordinal)],
                u.LastLoginAtUtc)),
        ];
        return Result.Success(summaries);
    }

    public async Task<Result<int>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanManage)
        {
            return Result.Failure<int>(_forbidden);
        }

        var fullName = request.FullName?.Trim() ?? string.Empty;
        var validation = ValidateFullName(fullName) ?? await ValidateRolesAsync(request.Roles, cancellationToken);
        if (validation is not null)
        {
            return Result.Failure<int>(validation);
        }

        return await db.InTransactionAsync(async () =>
        {
            var user = new ApplicationUser
            {
                UserName = request.UserName?.Trim(),
                FullName = fullName,
                IsActive = true,
                MustChangePassword = true,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            };

            var created = (await userManager.CreateAsync(user, request.TemporaryPassword)).ToResult();
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            var rolesAdded = (await userManager.AddToRolesAsync(user, request.Roles)).ToResult();
            if (rolesAdded.IsFailure)
            {
                return Result.Failure<int>(rolesAdded.Error!);
            }

            auditLog.Record(AuditActions.UserCreated, user.UserName, new { user.FullName, request.Roles });
            return Result.Success(user.Id);
        }, cancellationToken);
    }

    public async Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanManage)
        {
            return Result.Failure(_forbidden);
        }

        var fullName = request.FullName?.Trim() ?? string.Empty;
        var validation = ValidateFullName(fullName) ?? await ValidateRolesAsync(request.Roles, cancellationToken);
        if (validation is not null)
        {
            return Result.Failure(validation);
        }

        // Serializable: two administrators demoting each other at the same moment must not
        // both pass the "another active administrator exists" check.
        return await db.InTransactionAsync(async () =>
        {
            var user = await userManager.FindByIdAsync(request.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (user is null)
            {
                return Result.Failure(_notFound);
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            var wasAdministrator = currentRoles.Contains(SystemRoles.Administrator) && user.IsActive;
            var staysAdministrator = request.Roles.Contains(SystemRoles.Administrator) && request.IsActive;

            if (user.Id == currentUser.UserId)
            {
                if (!request.IsActive)
                {
                    return Result.Failure(new Error(UserErrors.CannotDeactivateSelf));
                }

                if (currentRoles.Contains(SystemRoles.Administrator) && !request.Roles.Contains(SystemRoles.Administrator))
                {
                    return Result.Failure(new Error(UserErrors.CannotRemoveOwnAdministratorRole));
                }
            }

            if (wasAdministrator && !staysAdministrator && !await OtherActiveAdministratorExistsAsync(user.Id, cancellationToken))
            {
                return Result.Failure(new Error(UserErrors.LastAdministrator));
            }

            var before = new { user.FullName, user.IsActive, Roles = currentRoles.Order(StringComparer.Ordinal).ToList() };
            var rolesToRemove = currentRoles.Except(request.Roles).ToList();
            var rolesToAdd = request.Roles.Except(currentRoles).ToList();
            var accessChanged = rolesToRemove.Count > 0 || rolesToAdd.Count > 0 || user.IsActive != request.IsActive;

            user.FullName = fullName;
            user.IsActive = request.IsActive;

            var steps = new List<Func<Task<IdentityResult>>>
            {
                () => userManager.RemoveFromRolesAsync(user, rolesToRemove),
                () => userManager.AddToRolesAsync(user, rolesToAdd),
                () => userManager.UpdateAsync(user),
            };

            // A new security stamp ends the user's open sessions, so changed access applies within a minute.
            if (accessChanged)
            {
                steps.Add(() => userManager.UpdateSecurityStampAsync(user));
            }

            foreach (var step in steps)
            {
                var stepResult = (await step()).ToResult();
                if (stepResult.IsFailure)
                {
                    return stepResult;
                }
            }

            var after = new { user.FullName, user.IsActive, Roles = request.Roles.Order(StringComparer.Ordinal).ToList() };
            auditLog.Record(AuditActions.UserUpdated, user.UserName, new { Before = before, After = after });
            return Result.Success();
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public async Task<Result> ResetPasswordAsync(int userId, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            return Result.Failure(_forbidden);
        }

        return await db.InTransactionAsync(async () =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return Result.Failure(_notFound);
            }

            // Remove + add (rather than a reset token) needs no token provider; the transaction
            // guarantees the user is never left without a password if the new one is rejected.
            var removed = (await userManager.RemovePasswordAsync(user)).ToResult();
            if (removed.IsFailure)
            {
                return removed;
            }

            var added = (await userManager.AddPasswordAsync(user, temporaryPassword)).ToResult();
            if (added.IsFailure)
            {
                return added;
            }

            user.MustChangePassword = true;
            var updated = (await userManager.UpdateAsync(user)).ToResult();
            if (updated.IsFailure)
            {
                return updated;
            }

            auditLog.Record(AuditActions.UserPasswordReset, user.UserName);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            return Result.Failure(_forbidden);
        }

        return await db.InTransactionAsync(async () =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return Result.Failure(_notFound);
            }

            var unlocked = (await userManager.SetLockoutEndDateAsync(user, null)).ToResult();
            if (unlocked.IsFailure)
            {
                return unlocked;
            }

            var reset = (await userManager.ResetAccessFailedCountAsync(user)).ToResult();
            if (reset.IsFailure)
            {
                return reset;
            }

            auditLog.Record(AuditActions.UserUnlocked, user.UserName);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RoleSummary>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            return Result.Failure<IReadOnlyList<RoleSummary>>(_forbidden);
        }

        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken);
        var permissions = (await db.RoleClaims.AsNoTracking()
                .Where(c => c.ClaimType == Permissions.ClaimType)
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.RoleId, c => c.ClaimValue!);
        var userCounts = await db.UserRoles.AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

        IReadOnlyList<RoleSummary> summaries =
        [
            .. roles.Select(r => new RoleSummary(
                r.Id,
                r.Name!,
                r.IsSystem,
                userCounts.GetValueOrDefault(r.Id),
                r.IsSystem ? Permissions.All : [.. permissions[r.Id].Order(StringComparer.Ordinal)])),
        ];
        return Result.Success(summaries);
    }

    public async Task<Result<int>> CreateRoleAsync(string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (!CanManage)
        {
            return Result.Failure<int>(_forbidden);
        }

        var roleName = name?.Trim() ?? string.Empty;
        var validation = ValidateRole(roleName, permissions);
        if (validation is not null)
        {
            return Result.Failure<int>(validation);
        }

        return await db.InTransactionAsync(async () =>
        {
            var role = new ApplicationRole(roleName);
            var created = (await roleManager.CreateAsync(role)).ToResult();
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            foreach (var permission in permissions.Distinct())
            {
                var added = (await roleManager.AddClaimAsync(role, new(Permissions.ClaimType, permission))).ToResult();
                if (added.IsFailure)
                {
                    return Result.Failure<int>(added.Error!);
                }
            }

            auditLog.Record(AuditActions.RoleCreated, role.Name, new { Permissions = permissions });
            return Result.Success(role.Id);
        }, cancellationToken);
    }

    public async Task<Result> UpdateRoleAsync(int roleId, string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (!CanManage)
        {
            return Result.Failure(_forbidden);
        }

        var roleName = name?.Trim() ?? string.Empty;
        var validation = ValidateRole(roleName, permissions);
        if (validation is not null)
        {
            return Result.Failure(validation);
        }

        return await db.InTransactionAsync(async () =>
        {
            var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);
            if (role is null)
            {
                return Result.Failure(_notFound);
            }

            if (role.IsSystem)
            {
                return Result.Failure(new Error(UserErrors.SystemRoleReadOnly));
            }

            var claims = await roleManager.GetClaimsAsync(role);
            var currentPermissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
            var before = new { role.Name, Permissions = currentPermissions.Order(StringComparer.Ordinal).ToList() };

            if (!string.Equals(role.Name, roleName, StringComparison.Ordinal))
            {
                var renamed = (await roleManager.SetRoleNameAsync(role, roleName)).ToResult();
                var saved = renamed.IsSuccess ? (await roleManager.UpdateAsync(role)).ToResult() : renamed;
                if (saved.IsFailure)
                {
                    return saved;
                }
            }

            foreach (var claim in claims.Where(c => c.Type == Permissions.ClaimType && !permissions.Contains(c.Value)))
            {
                var removed = (await roleManager.RemoveClaimAsync(role, claim)).ToResult();
                if (removed.IsFailure)
                {
                    return removed;
                }
            }

            foreach (var permission in permissions.Distinct().Except(currentPermissions))
            {
                var added = (await roleManager.AddClaimAsync(role, new(Permissions.ClaimType, permission))).ToResult();
                if (added.IsFailure)
                {
                    return added;
                }
            }

            // Members' sessions carry the old permissions; end them so the change applies within a minute.
            foreach (var member in await userManager.GetUsersInRoleAsync(roleName))
            {
                var stamped = (await userManager.UpdateSecurityStampAsync(member)).ToResult();
                if (stamped.IsFailure)
                {
                    return stamped;
                }
            }

            var after = new { Name = roleName, Permissions = permissions.Distinct().Order(StringComparer.Ordinal).ToList() };
            auditLog.Record(AuditActions.RoleUpdated, roleName, new { Before = before, After = after });
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            return Result.Failure(_forbidden);
        }

        return await db.InTransactionAsync(async () =>
        {
            var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);
            if (role is null)
            {
                return Result.Failure(_notFound);
            }

            if (role.IsSystem)
            {
                return Result.Failure(new Error(UserErrors.SystemRoleReadOnly));
            }

            if (await db.UserRoles.AnyAsync(ur => ur.RoleId == roleId, cancellationToken))
            {
                return Result.Failure(new Error(UserErrors.RoleInUse));
            }

            var deleted = (await roleManager.DeleteAsync(role)).ToResult();
            if (deleted.IsFailure)
            {
                return deleted;
            }

            auditLog.Record(AuditActions.RoleDeleted, role.Name);
            return Result.Success();
        }, cancellationToken);
    }

    private bool CanManage => currentUser.HasPermission(Permissions.UsersManage);

    private Task<bool> OtherActiveAdministratorExistsAsync(int userId, CancellationToken cancellationToken) =>
        (from userRole in db.UserRoles
         join role in db.Roles on userRole.RoleId equals role.Id
         join user in db.Users on userRole.UserId equals user.Id
         where role.Name == SystemRoles.Administrator && user.IsActive && user.Id != userId
         select user.Id).AnyAsync(cancellationToken);

    private static Error? ValidateFullName(string fullName) => fullName.Length switch
    {
        0 => new Error(UserErrors.FullNameRequired),
        > UserRules.FullNameMaxLength => new Error(UserErrors.FullNameTooLong, UserRules.FullNameMaxLength),
        _ => null,
    };

    private async Task<Error?> ValidateRolesAsync(IReadOnlyList<string>? roles, CancellationToken cancellationToken)
    {
        if (roles is null || roles.Count == 0)
        {
            return new Error(UserErrors.RoleRequired);
        }

        var existing = await db.Roles.Select(r => r.Name!).ToListAsync(cancellationToken);
        return roles.All(existing.Contains) ? null : new Error(UserErrors.UnknownRole);
    }

    private static Error? ValidateRole(string roleName, IReadOnlyList<string> permissions)
    {
        if (roleName.Length == 0)
        {
            return new Error(UserErrors.RoleNameRequired);
        }

        if (roleName.Length > UserRules.RoleNameMaxLength)
        {
            return new Error(IdentityErrors.InvalidRoleName);
        }

        return permissions.All(Permissions.IsDefined) ? null : new Error(UserErrors.UnknownPermission);
    }
}
