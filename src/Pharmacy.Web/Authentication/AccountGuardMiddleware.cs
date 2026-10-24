using Pharmacy.Application.Users;
using Pharmacy.Infrastructure.Identity;

namespace Pharmacy.Web.Authentication;

/// <summary>Remembers once the first administrator exists, so the setup check stops querying the database.</summary>
public sealed class SetupState
{
    private volatile bool _completed;

    public bool IsCompleted => _completed;

    public void MarkCompleted() => _completed = true;
}

/// <summary>
/// Steers page requests to the step the user must take first:
/// the one-time setup page while no user exists, then the change-password page while the
/// signed-in user still has a temporary password.
/// </summary>
public sealed class AccountGuardMiddleware(RequestDelegate next)
{
    public const string SetupPath = "/setup";
    public const string ChangePasswordPath = "/Account/ChangePassword";

    public async Task InvokeAsync(HttpContext context, SetupState setupState, IAccountService accountService)
    {
        if (!IsPageRequest(context.Request))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path;

        if (!setupState.IsCompleted)
        {
            if (await accountService.IsSetupRequiredAsync(context.RequestAborted))
            {
                if (!path.StartsWithSegments(SetupPath, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect(SetupPath);
                    return;
                }
            }
            else
            {
                setupState.MarkCompleted();
            }
        }

        if (context.User.HasClaim(PharmacyClaimTypes.MustChangePassword, "true")
            && !path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect(ChangePasswordPath);
            return;
        }

        await next(context);
    }

    // Only full page loads: not static files, framework/circuit traffic, or utility endpoints.
    private static bool IsPageRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? "/";
        return !path.StartsWith("/_", StringComparison.Ordinal)
            && !path.StartsWith("/culture/", StringComparison.OrdinalIgnoreCase)
            && !path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(path);
    }
}
