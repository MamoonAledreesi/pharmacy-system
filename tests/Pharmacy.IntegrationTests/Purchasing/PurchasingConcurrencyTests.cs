using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

/// <summary>Several people buying and paying at once: row locks serialize them and the books stay exact.</summary>
public sealed class PurchasingConcurrencyTests : PurchasingTestBase
{
    [Fact]
    public async Task EightPaymentsOfThirtyAgainstADebtOfHundred_SucceedExactlyThreeTimes()
    {
        var supplier = await CreateSupplierAsync();
        await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 100m, null)));
        await OpenShiftAsync(1000m);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => Payables(s => s.PayAsync(new PaySupplierRequest(Guid.NewGuid(), supplier, 30m, null))))));

        results.Count(r => r.IsSuccess).ShouldBe(3);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == PurchasingErrors.PaymentExceedsBalance);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(10m);
        (await DrawerAsync()).ShouldBe(910m);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task ParallelInvoices_GetDistinctGaplessNumbers_AndTheSupplierOwesTheSum()
    {
        var supplier = await CreateSupplierAsync();

        // The first invoice of a year creates the number sequence; racing it is retried by the user, so start it alone.
        await BuyAsync([BoxLine(1, 100m)], supplier);
        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(i =>
            Task.Run(() => Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m, batch: "B1")], supplier))))));

        results.ShouldAllBe(r => r.IsSuccess);
        var numbers = (await InvoicesAsync()).Select(i => i.Number).Order().ToList();
        numbers.ShouldBe(Enumerable.Range(1, 8).Select(n => $"PI-{Today.Year}-{n:D6}").ToList());
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(800m);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(24);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task ParallelCashPurchases_NeverOverdrawTheDrawer()
    {
        await OpenShiftAsync(500m);

        // Each invoice pays 120 in cash; only four fit in 500. The first one creates the number sequence and the batch.
        await BuyAsync([BoxLine(1, 120m)], paidCash: 120m);
        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => Purchasing(s => s.CreateAsync(Request([BoxLine(1, 120m)], paidCash: 120m))))));

        results.Count(r => r.IsSuccess).ShouldBe(3);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == CashErrors.InsufficientCash);
        (await DrawerAsync()).ShouldBe(20m);
        (await InvoicesAsync()).Count.ShouldBe(4);
        (await BatchesAsync()).Sum(b => b.QuantityAvailable).ShouldBe(12);
    }
}
