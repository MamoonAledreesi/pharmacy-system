using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class CatalogPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Cashier_CanOpenTheProductList_ThroughTheDefaultCatalogViewGrant()
    {
        await factory.CreateUserAsync("cashier", "Cash1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "cashier", "Cash1234");

        using var response = await browser.GetAsync("/catalog/products");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("الأدوية والمنتجات");
    }

    [Fact]
    public async Task UserWithoutCatalogPermission_IsDenied()
    {
        await factory.CreateUserAsync("norole", "None1234");
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "norole", "None1234");

        using var response = await browser.GetAsync("/catalog/products");

        response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task Administrator_SeesTheSeededReferenceLists()
    {
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, PharmacyWebFactory.AdminUserName, PharmacyWebFactory.AdminPassword);

        var html = await browser.GetStringAsync("/catalog/lists");

        html.ShouldContain("أدوية عامة");
        html.ShouldContain("وحدات القياس");
    }
}
