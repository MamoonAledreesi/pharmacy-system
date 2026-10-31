using System.Net;
using System.Xml.Linq;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Import;
using Pharmacy.Infrastructure.Import;
using Pharmacy.IntegrationTests.Web;

namespace Pharmacy.IntegrationTests.Import;

public sealed class ImportTemplateTests : ImportTestBase
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PharmacySystem.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> Resource(string language) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Pharmacy.Web", "Resources", $"SharedResource.{language}.resx"))
            .Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task TheExampleRowsOfTheTemplate_AreValidRowsThatImport(string language)
    {
        var texts = Resource(language);
        var headers = ProductColumns.All.Select(c => texts[ProductColumns.ResourcePrefix + c]).ToArray();
        IReadOnlyList<string> Example(string key) => [.. texts[key].Split(';')];
        var file = new ProductSheetWriter().Write(new ProductSheetText("Products", headers, [Example("Import.Example.Row1"), Example("Import.Example.Row2")], "Notes", [], language == "ar"));
        var sheet = new ProductSheetReader().Read(new MemoryStream(file));

        var preview = (await PreviewAsync([.. sheet.Rows])).Value;
        var result = await Services.InScopeAsync(s => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ProductImportService>(s).ImportAsync(sheet.Rows, Guid.NewGuid(), false));

        sheet.Rows.Count.ShouldBe(2);
        preview.ErrorCount.ShouldBe(0, string.Join("; ", preview.Rows.SelectMany(r => r.Errors).Select(e => e.Code)));
        (result.Value.ProductsCreated, result.Value.BatchesRecorded).ShouldBe((2, 1));
    }

    [Fact]
    public void EveryColumn_HasAHeaderInBothLanguages_AndTheNotesAreComplete()
    {
        foreach (var language in new[] { "en", "ar" })
        {
            var texts = Resource(language);
            ProductColumns.All.Where(c => !texts.ContainsKey(ProductColumns.ResourcePrefix + c)).ShouldBeEmpty(language);
            new[] { "Note.Title", "Note.Rows", "Note.Product", "Note.Units", "Note.Existing", "Note.Stock", "Note.Expiry", "Note.Dates", "Note.Again" }
                .Where(k => !texts.ContainsKey("Import." + k)).ShouldBeEmpty(language);
        }
    }
}

public sealed class ImportPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task ThePharmacist_OpensTheImportPage_AndDownloadsTheTemplateInArabic()
    {
        await factory.CreateUserAsync("imp1", "Import1234", SystemRoles.Pharmacist);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "imp1", "Import1234");

        var page = await browser.GetStringAsync("/catalog/import");
        var products = await browser.GetStringAsync("/catalog/products");
        using var template = await browser.GetAsync("/catalog/import/template");

        page.ShouldContain("data-test=\"download-template\"");
        page.ShouldContain("choose-file");
        products.ShouldContain("data-test=\"import-products\"");
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        template.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var sheet = new ProductSheetReader().Read(await template.Content.ReadAsStreamAsync());
        sheet.Rows.Count.ShouldBe(2);
        sheet.Rows[0].NameArabic.ShouldBe("أموكسيل");
        sheet.Rows[0].Category.ShouldBe("أدوية عامة");
    }

    [Fact]
    public async Task TheCashier_IsDeniedTheImportPageAndTheTemplate()
    {
        await factory.CreateUserAsync("imp2", "Import1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "imp2", "Import1234");

        using var page = await browser.GetAsync("/catalog/import");
        using var template = await browser.GetAsync("/catalog/import/template");
        var products = await browser.GetStringAsync("/catalog/products");

        page.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        template.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Redirect);
        products.ShouldNotContain("import-products");
    }

    [Fact]
    public async Task SignedOutVisitors_CannotDownloadTheTemplate()
    {
        var anonymous = factory.CreateBrowser();

        using var template = await anonymous.GetAsync("/catalog/import/template");

        template.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}
