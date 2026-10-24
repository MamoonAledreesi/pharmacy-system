using System.Net;
using Pharmacy.Domain.Settings;

namespace Pharmacy.IntegrationTests.Web;

public sealed class AppEndpointTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task Health_IsHealthyAndPublic()
    {
        using var response = await factory.CreateBrowser().GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task SignedInAdministrator_SeesPharmacyNameAndSettings()
    {
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, PharmacyWebFactory.AdminUserName, PharmacyWebFactory.AdminPassword);

        var home = await browser.GetStringAsync("/");
        var settings = await browser.GetStringAsync("/settings");

        home.ShouldContain($">{PharmacySettings.Defaults.NameArabic}</h6>");
        settings.ShouldContain("إعدادات الصيدلية");
        settings.ShouldContain("اسم الصيدلية بالعربية");
    }
}
