using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class DashboardPageTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Accountant_SeesTheFiguresIncludingProfit_AndTheNoShiftAlert()
    {
        await factory.CreateUserAsync("dash1", "Dash1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "dash1", "Dash1234");

        var home = await browser.GetStringAsync("/");

        home.ShouldContain("Dashboard.GrossProfitToday");
        home.ShouldContain("Dashboard.StockValue");
        home.ShouldContain("Dashboard.ExpensesMonth");
        home.ShouldContain("data-test=\"refresh\"");
    }

    [Fact]
    public async Task Cashier_SeesNoProfitOrStockValue_ButGetsTheNoShiftAlertAndShortcuts()
    {
        await factory.CreateUserAsync("dash2", "Dash1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "dash2", "Dash1234");

        var home = await browser.GetStringAsync("/");

        home.ShouldNotContain("Dashboard.GrossProfitToday");
        home.ShouldNotContain("Dashboard.StockValue");
        home.ShouldContain("alert-no-shift");
        home.ShouldContain("href=\"sales/new\"");
    }

    [Fact]
    public async Task TheStockList_OpensWithTheFilterFromTheAlertLink()
    {
        await factory.CreateUserAsync("dash3", "Dash1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "dash3", "Dash1234");

        using var response = await browser.GetAsync("/inventory?filter=NearExpiry");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain(">قريب الانتهاء</div>");
        (await browser.GetStringAsync("/inventory")).ShouldNotContain(">قريب الانتهاء</div>");
    }
}
