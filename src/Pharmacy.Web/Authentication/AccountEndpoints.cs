using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Pharmacy.Application.Auditing;
using Pharmacy.Infrastructure.Identity;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.Web.Authentication;

public static class AccountEndpoints
{
    public const string LoginPath = "/Account/Login";
    public const string LogoutPath = "/Account/Logout";
    public const string AccessDeniedPath = "/Account/AccessDenied";

    /// <summary>Logout is a POST with an antiforgery token, so a link or form on another site cannot sign users out.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LogoutPath, async (
            HttpContext context,
            [FromServices] IAntiforgery antiforgery,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAuditLog auditLog,
            [FromServices] PharmacyDbContext db) =>
        {
            // Minimal API endpoints validate antiforgery only when they bind form fields; this one
            // binds none, so validate explicitly.
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            auditLog.Record(AuditActions.Logout);
            await db.SaveChangesAsync();
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("~" + LoginPath);
        }).RequireAuthorization();

        return endpoints;
    }
}
