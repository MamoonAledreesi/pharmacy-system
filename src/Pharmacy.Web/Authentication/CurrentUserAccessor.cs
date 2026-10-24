using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;

namespace Pharmacy.Web.Authentication;

/// <summary>
/// Scoped holder of the signed-in user, read synchronously by application services.
/// HTTP requests set it from <see cref="HttpContext.User"/> (<see cref="CurrentUserMiddleware"/>);
/// interactive circuits set it from the authentication state (<see cref="CurrentUserCircuitHandler"/>),
/// because HttpContext is not available inside a circuit.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public ClaimsPrincipal Principal => _principal;

    public int? UserId =>
        int.TryParse(_principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public string? UserName => _principal.Identity?.IsAuthenticated == true ? _principal.Identity.Name : null;

    public bool HasPermission(string permission) => PermissionEvaluator.Has(_principal, permission);

    public void SetPrincipal(ClaimsPrincipal principal) => _principal = principal ?? throw new ArgumentNullException(nameof(principal));
}

public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, CurrentUserAccessor currentUser)
    {
        currentUser.SetPrincipal(context.User);
        return next(context);
    }
}

internal sealed class CurrentUserCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUserAccessor currentUser) : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        currentUser.SetPrincipal((await authenticationStateProvider.GetAuthenticationStateAsync()).User);
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task) => currentUser.SetPrincipal((await task).User);
}
