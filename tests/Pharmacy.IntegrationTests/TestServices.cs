using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application;
using Pharmacy.Application.Abstractions;
using Pharmacy.Infrastructure;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests;

/// <summary>Stand-in for the signed-in user. Tests set it before resolving services.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public int? UserId { get; set; }

    public string? UserName { get; set; }

    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    public bool IsAdministrator { get; set; }

    public bool HasPermission(string permission) => IsAdministrator || Permissions.Contains(permission);

    public void SignInAs(int? userId, string userName, bool isAdministrator = false, params string[] permissions)
    {
        UserId = userId;
        UserName = userName;
        IsAdministrator = isAdministrator;
        Permissions.Clear();
        Permissions.UnionWith(permissions);
    }

    public void SignInAsAdministrator(int userId = 0, string userName = "admin") =>
        SignInAs(userId, userName, isAdministrator: true);
}

/// <summary>
/// The real application and infrastructure services (no web host) over a migrated throwaway database.
/// </summary>
public sealed class TestServices : IAsyncLifetime
{
    private readonly string _connectionString = TestDatabase.CreateUniqueConnectionString();
    private ServiceProvider _provider = default!;

    public TestCurrentUser CurrentUser { get; } = new();

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Infrastructure.DependencyInjection.ConnectionStringName}"] = _connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddPharmacyIdentity();
        services.AddSingleton<ICurrentUser>(CurrentUser);
        _provider = services.BuildServiceProvider(validateScopes: true);

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    /// <summary>Runs <paramref name="work"/> with services from a fresh scope (a fresh DbContext, like a new request).</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) =>
        InScopeAsync<bool>(async services =>
        {
            await work(services);
            return true;
        });
}
