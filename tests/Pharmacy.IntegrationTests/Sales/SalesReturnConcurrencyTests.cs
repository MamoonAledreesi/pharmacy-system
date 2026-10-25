using Pharmacy.Domain.Sales;

namespace Pharmacy.IntegrationTests.Sales;

/// <summary>Several people returning the same invoice at once: the invoice lock makes them take turns.</summary>
public sealed class SalesReturnConcurrencyTests : ReturnsTestBase
{
    [Fact]
    public async Task EightReturnsOfTheSameTwoStrips_SucceedExactlyTwice()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(2, 15m)], paid: 30m);
        var line = await LineIdAsync(invoice);
        // The first return creates the number sequence; racing it is retried by the user, so start it alone.
        (await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)])))).IsSuccess.ShouldBeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)]))))));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == SalesErrors.ReturnQuantityExceeds);
        var returns = await ReturnsOfSalesAsync();
        returns.Count.ShouldBe(2);
        returns.Sum(r => r.Total).ShouldBe(30m);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
        (await DrawerAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task ParallelReturnsOfCreditSales_ReduceTheCustomerDebtExactly()
    {
        await StockAsync(30);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(8, 10m)], customerId: customer, paid: 0m)))).Value;
        var line = await LineIdAsync(invoice);
        (await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)])))).IsSuccess.ShouldBeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)]))))));

        results.ShouldAllBe(r => r.IsSuccess);
        (await CustomerBalanceAsync(customer)).ShouldBe(0m);
        (await ReturnsOfSalesAsync()).Sum(r => r.DebtReduction).ShouldBe(80m);
        var running = 0m;
        foreach (var entry in await CustomerEntriesAsync(customer))
        {
            running += entry.Amount;
            entry.BalanceAfter.ShouldBe(running);
        }
    }
}
