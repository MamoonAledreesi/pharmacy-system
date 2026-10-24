using System.Net;

namespace Pharmacy.IntegrationTests.Web;

public sealed class AppEndpointTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_IsHealthyOnceTheDatabaseIsMigrated()
    {
        using var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task AppBar_ShowsThePharmacyNameFromSettings()
    {
        var html = await _client.GetStringAsync("/");

        html.ShouldContain($">{Domain.Settings.PharmacySettings.Defaults.NameArabic}</h6>");
    }

    [Fact]
    public async Task SettingsPage_Renders()
    {
        var html = await _client.GetStringAsync("/settings");

        html.ShouldContain("إعدادات الصيدلية");
        html.ShouldContain("اسم الصيدلية بالعربية");
    }
}
