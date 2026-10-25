using System.Net;
using Pharmacy.Application.Authorization;

namespace Pharmacy.IntegrationTests.Web;

public sealed class ReportPagesTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public async Task Accountant_OpensTheReports_AndExportsThemToExcelAndPdf()
    {
        await factory.CreateUserAsync("books", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books", "Book1234");

        foreach (var path in new[] { "/reports", "/reports/Profit", "/reports/SalesDaily", "/reports/CustomerStatement", "/reports/ExpensesDetail" })
        {
            using var page = await browser.GetAsync(path);

            page.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        using var excel = await browser.GetAsync("/reports/Profit/export?format=xlsx");
        using var pdf = await browser.GetAsync("/reports/SalesDaily/export?format=pdf&from=2026-01-01&to=2026-12-31");

        excel.StatusCode.ShouldBe(HttpStatusCode.OK);
        excel.Content.Headers.ContentType!.MediaType.ShouldBe(ExcelType);
        excel.Content.Headers.ContentDisposition!.FileName.ShouldStartWith("report-Profit-");
        excel.Content.Headers.ContentDisposition.FileName.ShouldEndWith(".xlsx");
        (await excel.Content.ReadAsByteArrayAsync()).Take(2).ShouldBe("PK"u8.ToArray());
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).Take(4).ShouldBe("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task TheReportPage_ShowsTheTranslatedTable_AndTheHubListsTheReports()
    {
        await factory.CreateUserAsync("books4", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books4", "Book1234");

        var profit = await browser.GetStringAsync("/reports/Profit");
        var hub = await browser.GetStringAsync("/reports");
        var statement = await browser.GetStringAsync("/reports/CustomerStatement");

        profit.ShouldContain("<table");
        profit.ShouldContain("الربح التشغيلي");
        profit.ShouldContain("export-excel");
        hub.ShouldContain("href=\"reports/Profit\"");
        hub.ShouldContain("href=\"reports/SalesDaily\"");
        statement.ShouldNotContain("<table");
    }

    [Fact]
    public async Task Pharmacist_ReadsReports_ButCannotExportOrSeeProfit()
    {
        await factory.CreateUserAsync("pharm", "Pharm1234", SystemRoles.Pharmacist);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "pharm", "Pharm1234");

        using var hub = await browser.GetAsync("/reports");
        using var report = await browser.GetAsync("/reports/SalesDaily");
        using var export = await browser.GetAsync("/reports/SalesDaily/export?format=xlsx");
        using var profit = await browser.GetAsync("/reports/Profit/export?format=xlsx");

        hub.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        export.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        profit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cashier_IsDeniedTheReports()
    {
        await factory.CreateUserAsync("till", "Till1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "till", "Till1234");

        using var hub = await browser.GetAsync("/reports");
        using var export = await browser.GetAsync("/reports/SalesDaily/export?format=xlsx");

        hub.RedirectPath().ShouldStartWith("/Account/AccessDenied");
        export.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheExport_RejectsAnonymousUsers_UnknownReports_AndUnknownFormats()
    {
        var anonymous = factory.CreateBrowser();
        await factory.CreateUserAsync("books2", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books2", "Book1234");

        using var signedOut = await anonymous.GetAsync("/reports/SalesDaily/export?format=xlsx");
        using var unknownReport = await browser.GetAsync("/reports/Nothing/export?format=xlsx");
        using var unknownFormat = await browser.GetAsync("/reports/SalesDaily/export?format=doc");
        using var backwards = await browser.GetAsync("/reports/SalesDaily/export?format=pdf&from=2026-12-31&to=2026-01-01");

        signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        unknownReport.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownFormat.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheExport_FollowsTheChosenLanguage()
    {
        await factory.CreateUserAsync("books3", "Book1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "books3", "Book1234");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/reports/CustomerBalances/export?format=xlsx");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c%3Dar%7Cuic%3Dar");
        using var arabic = await browser.SendAsync(request);

        arabic.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await arabic.Content.ReadAsStreamAsync());
        workbook.Worksheet(1).Cell(1, 1).GetString().ShouldBe("أرصدة العملاء");
        workbook.Worksheet(1).RightToLeft.ShouldBeTrue();
    }
}
