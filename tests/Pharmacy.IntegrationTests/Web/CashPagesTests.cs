using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class CashPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Cashier_CanOpenTheCashPages()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        foreach (var path in new[] { "/cash", "/cash/shifts", "/cash/daily" })
        {
            using var response = await browser.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task CashRegisterPage_OffersToOpenAShift_WhenNoneIsOpen()
    {
        await factory.CreateUserAsync("till2", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till2", "Till1234");

        var html = await browser.GetStringAsync("/cash");

        html.ShouldContain("لا توجد وردية مفتوحة");
        html.ShouldContain("فتح وردية");
    }

    [Fact]
    public async Task UserWithoutCashView_IsDenied()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        using var response = await browser.GetAsync("/cash");

        response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }
}
