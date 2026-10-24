using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace Pharmacy.IntegrationTests.Web;

/// <summary>
/// Hosts the real application in memory and checks language selection end to end:
/// culture cookie → request culture → html lang/dir → localized text (on the public sign-in page).
/// </summary>
public sealed class LocalizationEndpointTests(PharmacyWebFactory factory)
    : IClassFixture<PharmacyWebFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task LoginPage_DefaultsToArabicRightToLeft()
    {
        var html = await _client.GetStringAsync("/Account/Login");

        html.ShouldContain("lang=\"ar\"");
        html.ShouldContain("dir=\"rtl\"");
        html.ShouldContain("نظام إدارة الصيدلية");
    }

    [Fact]
    public async Task LoginPage_WithEnglishCookie_IsEnglishLeftToRight()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Add(HeaderNames.Cookie, ".AspNetCore.Culture=c%3Den%7Cuic%3Den");

        using var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        html.ShouldContain("lang=\"en\"");
        html.ShouldContain("dir=\"ltr\"");
        html.ShouldContain("Pharmacy Management System");
    }

    [Fact]
    public async Task SetCulture_StoresCookieAndRedirectsBack()
    {
        using var response = await _client.GetAsync("/culture/set?culture=en&redirectUri=%2Fsales");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/sales");
        response.Headers.GetValues(HeaderNames.SetCookie).ShouldContain(cookie => cookie.Contains("uic%3Den"));
    }

    [Theory]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("https%3A%2F%2Fevil.example")]
    public async Task SetCulture_RejectsExternalRedirects(string redirectUri)
    {
        using var response = await _client.GetAsync($"/culture/set?culture=en&redirectUri={redirectUri}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task SetCulture_RejectsUnsupportedCulture()
    {
        using var response = await _client.GetAsync("/culture/set?culture=fr");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
