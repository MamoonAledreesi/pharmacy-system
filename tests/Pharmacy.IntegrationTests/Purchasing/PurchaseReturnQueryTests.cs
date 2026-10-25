using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class PurchaseReturnQueryTests : PurchaseReturnsTestBase
{
    [Fact]
    public async Task Returnable_ShowsWhatWasReceivedReturnedAndHeld_PerLine()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m, batch: "B7")], supplier);
        var line = await LineIdAsync(invoice);
        await QuarantineAsync((await BatchesAsync()).Single().Id, 1);
        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])));

        var returnable = (await PurchaseReturnQuery(s => s.GetReturnableAsync(invoice))).Value;

        var item = returnable.Lines.Single();
        item.ReceivedBaseQuantity.ShouldBe(6);
        item.ReturnedBaseQuantity.ShouldBe(2);
        item.RemainingBaseQuantity.ShouldBe(4);
        item.BatchNumber.ShouldBe("B7");
        item.BatchAvailable.ShouldBe(3);
        item.BatchQuarantined.ShouldBe(1);
        item.LineTotal.ShouldBe(240m);
        item.Units.Select(u => u.Factor).ShouldBe([1, 3]);
        returnable.SupplierName.ShouldBe("Al-Noor Pharma");
        returnable.OnCredit.ShouldBe(240m);
    }

    [Fact]
    public async Task Search_FiltersByNumberInvoiceSupplierAndDate_NewestFirst()
    {
        var first = await CreateSupplierAsync("Al-Noor Pharma");
        var second = await CreateSupplierAsync("Delta Pharma");
        var invoiceA = await BuyAsync([BoxLine(2, 120m)], first);
        var invoiceB = await BuyAsync([BoxLine(2, 100m, batch: "B2")], second);
        var lineA = await LineIdAsync(invoiceA);
        var lineB = await LineIdAsync(invoiceB);
        var a = (await PurchaseReturns(s => s.CreateAsync(SendBack(invoiceA, [Out(lineA, 1)])))).Value;
        var b = (await PurchaseReturns(s => s.CreateAsync(SendBack(invoiceB, [Out(lineB, 1)])))).Value;

        var all = (await PurchaseReturnQuery(s => s.SearchAsync(new PurchaseReturnQuery(null, null, null, null, 0, 25)))).Value;
        var bySupplier = (await PurchaseReturnQuery(s => s.SearchAsync(new PurchaseReturnQuery("Delta", null, null, null, 0, 25)))).Value;
        var byInvoice = (await PurchaseReturnQuery(s => s.SearchAsync(new PurchaseReturnQuery(null, invoiceA, null, null, 0, 25)))).Value;
        var past = (await PurchaseReturnQuery(s => s.SearchAsync(new PurchaseReturnQuery(null, null, Today.AddDays(-3), Today.AddDays(-1), 0, 25)))).Value;

        all.Items.Select(i => i.Id).ShouldBe([b, a]);
        bySupplier.Items.Select(i => i.Id).ShouldBe([b]);
        byInvoice.Items.Select(i => i.Id).ShouldBe([a]);
        past.TotalCount.ShouldBe(0);
        all.Items.Last().SupplierName.ShouldBe("Al-Noor Pharma");
    }

    [Fact]
    public async Task Details_ShowTheLinesBatchesAndSource()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m, batch: "B9")], supplier);
        var line = await LineIdAsync(invoice);
        await QuarantineAsync((await BatchesAsync()).Single().Id, 2);
        var id = (await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2, fromQuarantine: true)], reason: "expired on arrival")))).Value;

        var detail = (await PurchaseReturnQuery(s => s.GetAsync(id))).Value;

        detail.Total.ShouldBe(80m);
        detail.Reason.ShouldBe("expired on arrival");
        detail.SupplierName.ShouldBe("Al-Noor Pharma");
        var item = detail.Lines.Single();
        item.Product.English.ShouldBe("Panadol");
        item.BatchNumber.ShouldBe("B9");
        item.FromQuarantine.ShouldBeTrue();
        item.Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task Queries_WithoutPermission_AreForbidden()
    {
        SignIn();

        (await PurchaseReturnQuery(s => s.SearchAsync(new PurchaseReturnQuery(null, null, null, null, 0, 25)))).IsFailure.ShouldBeTrue();
        (await PurchaseReturnQuery(s => s.GetAsync(1))).IsFailure.ShouldBeTrue();
        SignIn(Permissions.PurchasingView);
        (await PurchaseReturnQuery(s => s.GetReturnableAsync(1))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }
}
