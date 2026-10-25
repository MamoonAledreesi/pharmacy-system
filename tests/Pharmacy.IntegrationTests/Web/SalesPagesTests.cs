using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class SalesPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Cashier_CanOpenTheSalesPages()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        foreach (var path in new[] { "/sales", "/sales/new", "/sales/999", "/sales/999/print" })
        {
            using var response = await browser.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Accountant_CanReadSales_ButCannotStartASale()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        using var list = await browser.GetAsync("/sales");
        using var create = await browser.GetAsync("/sales/new");

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        create.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task StoreKeeper_IsDeniedTheSalesPages()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        foreach (var path in new[] { "/sales", "/sales/new", "/sales/1/print" })
        {
            using var response = await browser.GetAsync(path);

            response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        }
    }
}
