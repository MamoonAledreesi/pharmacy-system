using System.Net;
using Pharmacy.Application.Authorization;
namespace Pharmacy.IntegrationTests.Web;

public sealed class PurchasingPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{

    [Fact]
    public async Task StoreKeeper_CanOpenTheSupplierAndPurchasePages()
    {
        await factory.CreateUserAsync("keeper", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper", "Keep1234");

        foreach (var path in new[] { "/suppliers", "/purchases", "/purchases/new" })
        {
            using var response = await browser.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }


    [Fact]
    public async Task StoreKeeper_SeesTheAddSupplierButton_ButNotTheStatementActionsOfAnAccountant()
    {
        await factory.CreateUserAsync("keeper2", "Keep1234", SystemRoles.StoreKeeper);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "keeper2", "Keep1234");

        var html = await browser.GetStringAsync("/suppliers");

        html.ShouldContain("إضافة مورد");
    }


    [Fact]
    public async Task Accountant_CanViewSuppliersAndPurchases_ButCannotCreateAPurchase()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        using var suppliers = await browser.GetAsync("/suppliers");
        using var purchases = await browser.GetAsync("/purchases");
        using var create = await browser.GetAsync("/purchases/new");

        suppliers.StatusCode.ShouldBe(HttpStatusCode.OK);
        purchases.StatusCode.ShouldBe(HttpStatusCode.OK);
        create.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task Cashier_IsDeniedTheSupplierAndPurchasePages()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        foreach (var path in new[] { "/suppliers", "/purchases", "/purchases/new" })
        {
            using var response = await browser.GetAsync(path);

            response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        }
    }
}
