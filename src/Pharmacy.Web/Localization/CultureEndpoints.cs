using Microsoft.AspNetCore.Localization;

namespace Pharmacy.Web.Localization;

public static class CultureEndpoints
{
    public const string SetCulturePath = "/culture/set";

    /// <summary>
    /// Stores the chosen language in the culture cookie and redirects back.
    /// A full page load is required so the new culture and text direction apply to the whole circuit.
    /// </summary>
    public static IEndpointRouteBuilder MapCultureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SetCulturePath, (string culture, string? redirectUri, HttpContext httpContext) =>
        {
            if (!SupportedCultures.IsSupported(culture))
            {
                return Results.BadRequest();
            }

            httpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture.ToLowerInvariant())),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = httpContext.Request.IsHttps,
                });

            return Results.LocalRedirect(IsLocalPath(redirectUri) ? redirectUri! : "/");
        });

        return endpoints;
    }

    // Prevents open redirects: only app-relative paths such as "/sales" are accepted.
    private static bool IsLocalPath(string? path) =>
        !string.IsNullOrEmpty(path)
        && path[0] == '/'
        && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'));
}
