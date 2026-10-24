using Microsoft.AspNetCore.Identity;
using Pharmacy.Application.Users;
using Pharmacy.Domain.Common;

namespace Pharmacy.Infrastructure.Identity;

internal static class IdentityResultExtensions
{
    private static readonly HashSet<string> _localizedCodes =
    [
        nameof(IdentityErrorDescriber.DuplicateUserName),
        nameof(IdentityErrorDescriber.InvalidUserName),
        nameof(IdentityErrorDescriber.PasswordTooShort),
        nameof(IdentityErrorDescriber.PasswordRequiresDigit),
        nameof(IdentityErrorDescriber.PasswordMismatch),
        nameof(IdentityErrorDescriber.DuplicateRoleName),
        nameof(IdentityErrorDescriber.InvalidRoleName),
        nameof(IdentityErrorDescriber.ConcurrencyFailure),
        PasswordLetterValidator.ErrorCode,
    ];

    /// <summary>Maps the first Identity error to a localizable error code ("Identity.{Code}").</summary>
    public static Result ToResult(this IdentityResult result)
    {
        if (result.Succeeded)
        {
            return Result.Success();
        }

        var error = result.Errors.First();
        if (!_localizedCodes.Contains(error.Code))
        {
            return Result.Failure(new Error(IdentityErrors.DefaultError));
        }

        var code = $"Identity.{error.Code}";
        return error.Code == nameof(IdentityErrorDescriber.PasswordTooShort)
            ? Result.Failure(new Error(code, UserRules.PasswordMinLength))
            : Result.Failure(new Error(code));
    }
}
