using Pharmacy.Domain.Receivables;

namespace Pharmacy.IntegrationTests.Receivables;

/// <summary>Several cashiers collecting at once: row locks serialize them and the books stay exact.</summary>
public sealed class ReceivablesConcurrencyTests : ReceivablesTestBase
{
    [Fact]
    public async Task EightCollectionsOfThirtyAgainstADebtOfHundred_SucceedExactlyThreeTimes()
    {
        var customer = await CreateDebtorAsync(100m);
        await OpenAsync(0m, note: null);
        // The first collection creates the number sequence; racing it is retried by the user, so start it alone.
        (await CollectAsync(customer, 30m)).IsSuccess.ShouldBeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => Task.Run(() => CollectAsync(customer, 30m))));

        results.Count(r => r.IsSuccess).ShouldBe(2);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == ReceivablesErrors.CollectionExceedsBalance);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(10m);
        (await DrawerAsync()).ShouldBe(90m);
        await AssertStatementMatchesBalanceAsync(customer);
    }

    [Fact]
    public async Task ParallelCollections_GetDistinctGaplessNumbers()
    {
        var customer = await CreateDebtorAsync(1000m);
        await OpenAsync(0m, note: null);
        await CollectAsync(customer, 10m);

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => Task.Run(() => CollectAsync(customer, 10m))));

        results.ShouldAllBe(r => r.IsSuccess);
        var entries = await EntriesAsync(customer);
        entries.Count(e => e.Type == CustomerEntryType.Collection).ShouldBe(8);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(920m);
        await AssertStatementMatchesBalanceAsync(customer);
    }
}
