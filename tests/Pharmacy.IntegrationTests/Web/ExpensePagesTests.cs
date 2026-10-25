using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class ExpensePagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Accountant_CanOpenTheExpensePages()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        foreach (var path in new[] { "/expenses", "/expenses/categories" })
        {
            using var response = await browser.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Pharmacist_CanReadExpenses_ButCannotManageCategories()
    {
        await factory.CreateUserAsync("pharm", "Pharm1234", SystemRoles.Pharmacist);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "pharm", "Pharm1234");

        using var list = await browser.GetAsync("/expenses");
        using var categories = await browser.GetAsync("/expenses/categories");

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        categories.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task Cashier_IsDeniedTheExpensePages()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        foreach (var path in new[] { "/expenses", "/expenses/categories" })
        {
            using var response = await browser.GetAsync(path);

            response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        }
    }
}
