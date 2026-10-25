using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class SalesReturnPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Cashier_CanOpenTheReturnPages()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        foreach (var path in new[] { "/sales/returns", "/sales/returns/999", "/sales/999/return" })
        {
            using var response = await browser.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Accountant_CanReadReturns_ButCannotStartOne()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        using var list = await browser.GetAsync("/sales/returns");
        using var create = await browser.GetAsync("/sales/1/return");

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        create.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task StoreKeeper_IsDeniedTheReturnPages()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        foreach (var path in new[] { "/sales/returns", "/sales/1/return" })
        {
            using var response = await browser.GetAsync(path);

            response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        }
    }
}
