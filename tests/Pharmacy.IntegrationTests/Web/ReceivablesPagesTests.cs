using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class ReceivablesPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Cashier_CanOpenTheCustomerPages_AndSeesTheDefaultCustomer()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        using var list = await browser.GetAsync("/customers");
        var details = await browser.GetStringAsync("/customers/1");

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        details.ShouldContain("عميل نقدي");
    }

    [Fact]
    public async Task Cashier_IsNotOfferedTheAddCustomerButton()
    {
        await factory.CreateUserAsync("till2", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till2", "Till1234");

        var html = await browser.GetStringAsync("/customers");

        html.ShouldNotContain("إضافة عميل");
    }

    [Fact]
    public async Task Pharmacist_IsOfferedTheAddCustomerButton()
    {
        await factory.CreateUserAsync("pharm", "Pharm1234", SystemRoles.Pharmacist);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "pharm", "Pharm1234");

        var html = await browser.GetStringAsync("/customers");

        html.ShouldContain("إضافة عميل");
    }

    [Fact]
    public async Task StoreKeeper_IsDeniedTheCustomerPages()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        using var list = await browser.GetAsync("/customers");
        using var details = await browser.GetAsync("/customers/1");

        list.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        details.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }
}
