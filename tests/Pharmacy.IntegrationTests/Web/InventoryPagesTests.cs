using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class InventoryPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task StoreKeeper_SeesTheStockPageAndTheOpeningStockButton()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        using var response = await browser.GetAsync("/inventory");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("إضافة مخزون افتتاحي");
    }

    [Fact]
    public async Task Cashier_CanViewStock_ButIsNotOfferedOpeningStockOrCosts()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        using var response = await browser.GetAsync("/inventory");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("إضافة مخزون افتتاحي");
        html.ShouldNotContain("قيمة المخزون");
    }

    [Fact]
    public async Task Accountant_SeesTheStockValueColumn()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        var html = await browser.GetStringAsync("/inventory");

        html.ShouldContain("قيمة المخزون");
        html.ShouldNotContain("إضافة مخزون افتتاحي");
    }

    [Fact]
    public async Task UserWithoutInventoryView_IsDenied_AndThePageIsHiddenFromTheMenu()
    {
        await factory.CreateUserAsync("norole", "None1234");
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "norole", "None1234");

        using var stock = await browser.GetAsync("/inventory");
        using var product = await browser.GetAsync("/inventory/1");

        stock.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        product.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task AnonymousUser_IsSentToSignIn()
    {
        using var response = await factory.CreateBrowser().GetAsync("/inventory");

        response.RedirectPath().ShouldStartWith("/Account/Login");
    }

    [Fact]
    public async Task TheSettingsPage_OffersTheNearExpiryPeriod()
    {
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, PharmacyWebFactory.AdminUserName, PharmacyWebFactory.AdminPassword);

        var html = await browser.GetStringAsync("/settings");

        html.ShouldContain("تنبيه قرب الانتهاء (أيام)");
    }
}
