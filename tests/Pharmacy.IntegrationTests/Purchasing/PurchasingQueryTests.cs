using Pharmacy.Application.Authorization;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class PurchasingQueryTests : PurchasingTestBase
{
    [Fact]
    public async Task Search_FindsSuppliersByNormalizedNameAndHidesInactiveOnes()
    {
        await CreateSupplierAsync("شركة الأمل للأدوية");
        var other = await CreateSupplierAsync("Delta Pharma");
        var detail = (await SupplierQuery(s => s.GetAsync(other))).Value;
        await Suppliers(s => s.UpdateAsync(other, new SupplierInput(detail.Name, null, null, null, false), detail.RowVersion));

        var byArabic = (await SupplierQuery(s => s.SearchAsync(new SupplierQuery("الامل", false, 0, 25)))).Value;
        var activeOnly = (await SupplierQuery(s => s.SearchAsync(new SupplierQuery(null, false, 0, 25)))).Value;
        var all = (await SupplierQuery(s => s.SearchAsync(new SupplierQuery(null, true, 0, 25)))).Value;

        byArabic.Items.Select(i => i.Name).ShouldBe(["شركة الأمل للأدوية"]);
        activeOnly.TotalCount.ShouldBe(1);
        all.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task QuickSearch_PutsNamesThatStartWithTheTextFirst()
    {
        await CreateSupplierAsync("Zeta Pharma");
        await CreateSupplierAsync("Pharma Plus");

        var found = (await SupplierQuery(s => s.QuickSearchAsync("pharma"))).Value;

        found.Select(i => i.Name).ShouldBe(["Pharma Plus", "Zeta Pharma"]);
    }

    [Fact]
    public async Task Statement_ListsNewestFirstWithRunningBalancesAndInvoiceNumbers()
    {
        var supplier = await CreateSupplierAsync();
        await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 100m, null)));
        await BuyAsync([BoxLine(1, 50m)], supplier);
        await OpenShiftAsync(1000m);
        await Payables(s => s.PayAsync(new PaySupplierRequest(Guid.NewGuid(), supplier, 30m, null)));

        var page = (await SupplierQuery(s => s.GetStatementAsync(new StatementQuery(supplier, 0, 25)))).Value;

        page.TotalCount.ShouldBe(3);
        page.Items.Select(e => e.Type).ShouldBe([SupplierEntryType.Payment, SupplierEntryType.Invoice, SupplierEntryType.OpeningBalance]);
        page.Items.Select(e => e.BalanceAfter).ShouldBe([120m, 150m, 100m]);
        page.Items[1].ReferenceNumber.ShouldBe($"PI-{Today.Year}-000001");
        page.Items[0].PaymentReversed.ShouldBeFalse();
    }

    [Fact]
    public async Task Statement_MarksReversedPayments()
    {
        var supplier = await CreateSupplierAsync();
        await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 100m, null)));
        await OpenShiftAsync(1000m);
        var payment = (await Payables(s => s.PayAsync(new PaySupplierRequest(Guid.NewGuid(), supplier, 30m, null)))).Value;
        await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(Guid.NewGuid(), payment, "typed twice")));

        var page = (await SupplierQuery(s => s.GetStatementAsync(new StatementQuery(supplier, 0, 25)))).Value;

        page.Items.Single(e => e.Type == SupplierEntryType.Payment).PaymentReversed.ShouldBeTrue();
    }

    [Fact]
    public async Task Details_ReportTheBalanceAndWhetherAnOpeningBalanceExists()
    {
        var supplier = await CreateSupplierAsync();
        var before = (await SupplierQuery(s => s.GetAsync(supplier))).Value;
        await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 100m, null)));
        var after = (await SupplierQuery(s => s.GetAsync(supplier))).Value;

        before.HasOpeningBalance.ShouldBeFalse();
        after.HasOpeningBalance.ShouldBeTrue();
        after.Balance.ShouldBe(100m);
    }

    [Fact]
    public async Task PurchaseList_FiltersBySupplierAndText_AndDetailsShowTheLines()
    {
        var first = await CreateSupplierAsync("Al-Noor Pharma");
        var second = await CreateSupplierAsync("Delta Pharma");
        var invoiceId = await BuyAsync([BoxLine(2, 120m)], first);
        await BuyAsync([BoxLine(1, 100m, batch: "B2")], second);

        var forFirst = (await PurchaseQuery(s => s.SearchAsync(new PurchaseQuery(null, first, null, null, 0, 25)))).Value;
        var byName = (await PurchaseQuery(s => s.SearchAsync(new PurchaseQuery("Delta", null, null, null, 0, 25)))).Value;
        var detail = (await PurchaseQuery(s => s.GetAsync(invoiceId))).Value;

        forFirst.Items.Select(i => i.Id).ShouldBe([invoiceId]);
        forFirst.Items.Single().SupplierName.ShouldBe("Al-Noor Pharma");
        byName.TotalCount.ShouldBe(1);
        detail.Total.ShouldBe(240m);
        detail.SupplierName.ShouldBe("Al-Noor Pharma");
        var line = detail.Lines.Single();
        line.Quantity.ShouldBe(2);
        line.BaseQuantity.ShouldBe(6);
        line.CostPerUnit.ShouldBe(120m);
        line.Product.English.ShouldBe("Panadol");
        line.BatchNumber.ShouldBe("B1");
    }

    [Fact]
    public async Task ProductForPurchase_ReportsUnitsExpiryAndTheLatestCost()
    {
        var supplier = await CreateSupplierAsync();
        await BuyAsync([BoxLine(1, 120m)], supplier);

        var product = (await PurchaseQuery(s => s.GetProductAsync(ProductId))).Value;

        product.TracksExpiry.ShouldBeTrue();
        product.Units.Select(u => u.Factor).ShouldBe([1, 3]);
        product.LastBaseUnitCost.ShouldBe(40m);
    }

    [Fact]
    public async Task Queries_WithoutViewPermission_AreForbidden()
    {
        SignIn();

        (await SupplierQuery(s => s.SearchAsync(new SupplierQuery(null, false, 0, 25)))).IsFailure.ShouldBeTrue();
        (await PurchaseQuery(s => s.SearchAsync(new PurchaseQuery(null, null, null, null, 0, 25)))).IsFailure.ShouldBeTrue();
        SignIn(Permissions.PurchasingView);
        (await PurchaseQuery(s => s.GetProductAsync(ProductId))).IsFailure.ShouldBeTrue();
    }
}
