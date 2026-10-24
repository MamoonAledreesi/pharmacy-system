using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.Web.Authentication;
using Pharmacy.Web.Common;

namespace Pharmacy.IntegrationTests.Web;

/// <summary>
/// Blazor components of one page share a DI scope and load data concurrently (a layout and its page do).
/// These tests pin down why components go through <see cref="ServiceRunner"/>.
/// </summary>
public sealed class ServiceRunnerTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task ConcurrentCallsFromOneScope_EachGetTheirOwnDbContext()
    {
        await using var componentScope = factory.Services.CreateAsyncScope();
        componentScope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().SetPrincipal(Administrator());
        var runner = componentScope.ServiceProvider.GetRequiredService<ServiceRunner>();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            runner.For<ProductQueryService>().RunAsync(s => s.SearchAsync(new ProductQuery(null, null, false, 0, 25)))));

        results.ShouldAllBe(r => r.IsSuccess, "the caller's user flows into each operation's scope");
    }

    [Fact]
    public async Task ConcurrentQueriesOnASharedDbContext_Fail_WhichIsWhyComponentsUseTheRunner()
    {
        await using var componentScope = factory.Services.CreateAsyncScope();
        var shared = componentScope.ServiceProvider.GetRequiredService<PharmacyDbContext>();

        var concurrent = () => Task.WhenAll(Enumerable.Range(0, 10).Select(_ => shared.Products.CountAsync()));

        await Should.ThrowAsync<InvalidOperationException>(concurrent);
    }

    private static ClaimsPrincipal Administrator() =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, SystemRoles.Administrator)],
            authenticationType: "Test"));
}
