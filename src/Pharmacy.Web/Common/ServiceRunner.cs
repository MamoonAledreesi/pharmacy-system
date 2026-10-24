using Pharmacy.Web.Authentication;

namespace Pharmacy.Web.Common;

/// <summary>
/// Runs each application-service call in its own DI scope, so it gets its own DbContext (a unit of work).
/// <para>
/// Blazor components of one page share a single scope (the request, or the whole interactive circuit),
/// and a layout and its page initialize concurrently. Calling services that share one scoped DbContext
/// from several components therefore fails with "a second operation was started on this context".
/// This follows Microsoft's guidance for EF Core in Blazor (one context per operation), while keeping
/// everything inside one operation (service, audit entry, Identity managers) on the same context and transaction.
/// </para>
/// Components call services as: <c>await Services.For&lt;ProductService&gt;().RunAsync(s =&gt; s.SearchAsync(query))</c>.
/// </summary>
public sealed class ServiceRunner(IServiceScopeFactory scopeFactory, CurrentUserAccessor currentUser)
{
    public ScopedCall<TService> For<TService>()
        where TService : notnull => new(this);

    internal async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> operation)
        where TService : notnull
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        // The new scope has its own current-user holder; give it the caller's signed-in user.
        scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().SetPrincipal(currentUser.Principal);

        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
}

/// <summary>Lets the result type be inferred from the call: <c>Services.For&lt;T&gt;().RunAsync(s =&gt; ...)</c>.</summary>
public readonly struct ScopedCall<TService>(ServiceRunner runner)
    where TService : notnull
{
    public Task<TResult> RunAsync<TResult>(Func<TService, Task<TResult>> operation) =>
        runner.RunAsync(operation);
}
