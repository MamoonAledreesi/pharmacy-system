namespace Pharmacy.Web.Common;

/// <summary>
/// Adds browser security headers to every response. The policy allows only this site's own scripts, styles, images and
/// fonts (the system runs on a LAN and uses no CDN) and the WebSocket Blazor Server needs; inline script and style stay
/// allowed because the framework and the print buttons use them.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                return Task.CompletedTask;
            });

            await next();
        });
}
