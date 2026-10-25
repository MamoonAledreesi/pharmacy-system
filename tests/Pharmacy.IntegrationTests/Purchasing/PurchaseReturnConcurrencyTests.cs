using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

/// <summary>Several people returning the same purchase at once: the invoice lock makes them take turns.</summary>
public sealed class PurchaseReturnConcurrencyTests : PurchaseReturnsTestBase
{
    [Fact]
    public async Task SevenReturnsOfTheLastStripOfAnInvoice_SucceedExactlyOnce()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(1, 120m)], supplier);
        var line = await LineIdAsync(invoice);
        // The first return creates the number sequence; racing it is retried by the user, so start it alone.
        (await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])))).IsSuccess.ShouldBeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1)]))))));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == PurchasingErrors.ReturnQuantityExceeds);
        (await ReturnsOfPurchasesAsync()).Sum(r => r.Total).ShouldBe(120m);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(0);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
        await AssertStatementMatchesBalanceAsync(supplier);
    }
}
