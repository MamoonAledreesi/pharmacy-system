using Microsoft.AspNetCore.Identity;
using Pharmacy.Application.Users;

namespace Pharmacy.Infrastructure.Identity;

/// <summary>
/// Requires at least one letter in any script (Arabic or Latin). Identity's built-in options can only
/// demand specifically lowercase or uppercase Latin letters, which would reject Arabic-only passwords.
/// </summary>
public sealed class PasswordLetterValidator : IPasswordValidator<ApplicationUser>
{
    public const string ErrorCode = "PasswordRequiresLetter";

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var result = password is not null && password.Any(char.IsLetter)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = IdentityErrors.PasswordRequiresLetter });

        return Task.FromResult(result);
    }
}
