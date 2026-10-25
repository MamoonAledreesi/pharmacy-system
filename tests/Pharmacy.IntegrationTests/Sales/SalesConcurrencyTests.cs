using Pharmacy.Domain.Inventory;

namespace Pharmacy.IntegrationTests.Sales;

/// <summary>Several cashiers selling at once: row locks serialize them, stock never goes negative, and numbers never repeat.</summary>
public sealed class SalesConcurrencyTests : SalesTestBase
{
    [Fact]
    public async Task EightCashiersSellingTheLastFiveStrips_SellExactlyFive()
    {
        await StockAsync(6);
        await OpenShiftAsync(0m);
        // The first sale creates the number sequence; racing it is retried by the user, so start it alone.
        await SellAsync([Strips(1, 20m)], 20m);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], paid: 20m))))));

        results.Count(r => r.IsSuccess).ShouldBe(5);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == InventoryErrors.InsufficientStock);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(0);
        var invoices = await InvoicesOfSalesAsync();
        invoices.Count.ShouldBe(6);
        invoices.Select(i => i.Number).Order().ShouldBe(Enumerable.Range(1, 6).Select(n => $"SI-{DateTime.Now.Year}-{n:D6}").ToList());
        (await DrawerAsync()).ShouldBe(120m);
        (await MovementsAsync()).Where(m => m.Type == StockMovementType.Sale).Sum(m => m.AvailableDelta).ShouldBe(-6);
    }

    [Fact]
    public async Task ParallelCreditSales_ToOneCustomer_AddUpExactly()
    {
        await StockAsync(20);
        var customer = await CreateCustomerAsync();
        await Sales(s => s.CreateAsync(Sale([Strips(1, 10m)], customerId: customer, paid: 0m)));

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => Sales(s => s.CreateAsync(Sale([Strips(1, 10m)], customerId: customer, paid: 0m))))));

        results.ShouldAllBe(r => r.IsSuccess);
        (await CustomerBalanceAsync(customer)).ShouldBe(80m);
        var running = 0m;
        foreach (var entry in await CustomerEntriesAsync(customer))
        {
            running += entry.Amount;
            entry.BalanceAfter.ShouldBe(running);
        }
    }
}
