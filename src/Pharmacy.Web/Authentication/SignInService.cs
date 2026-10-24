using Microsoft.AspNetCore.Identity;
using Pharmacy.Application.Auditing;
using Pharmacy.Infrastructure.Identity;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.Web.Authentication;

public enum SignInOutcome
{
    Succeeded,
    MustChangePassword,
    LockedOut,

    /// <summary>Wrong user name or password, or a deactivated account. Deliberately indistinguishable to the user.</summary>
    Failed,
}

/// <summary>Password sign-in with lockout, last-login tracking, and an audit entry for every attempt.</summary>
public sealed class SignInService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    PharmacyDbContext db,
    IAuditLog auditLog,
    TimeProvider clock)
{
    public async Task<SignInOutcome> PasswordSignInAsync(string userName, string password)
    {
        userName = userName?.Trim() ?? string.Empty;

        // lockoutOnFailure: 5 wrong passwords lock the account for 15 minutes (approved policy).
        var result = await signInManager.PasswordSignInAsync(userName, password ?? string.Empty, isPersistent: false, lockoutOnFailure: true);
        var user = await userManager.FindByNameAsync(userName);

        SignInOutcome outcome;
        if (result.Succeeded && user is not null)
        {
            user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
            await userManager.UpdateAsync(user);
            auditLog.RecordFor(user.Id, user.UserName!, AuditActions.LoginSucceeded, user.UserName);
            outcome = user.MustChangePassword ? SignInOutcome.MustChangePassword : SignInOutcome.Succeeded;
        }
        else if (result.IsLockedOut)
        {
            auditLog.RecordFor(user?.Id, userName, AuditActions.LockedOut, userName);
            outcome = SignInOutcome.LockedOut;
        }
        else
        {
            var reason = result.IsNotAllowed ? "Inactive" : user is null ? "UnknownUser" : "WrongPassword";
            auditLog.RecordFor(user?.Id, userName, AuditActions.LoginFailed, userName, new { Reason = reason });
            outcome = SignInOutcome.Failed;
        }

        await db.SaveChangesAsync();
        return outcome;
    }
}
