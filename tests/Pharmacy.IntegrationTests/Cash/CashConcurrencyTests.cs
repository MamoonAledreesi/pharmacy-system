using Pharmacy.Application.Cash;
using Pharmacy.Domain.Cash;

namespace Pharmacy.IntegrationTests.Cash;

/// <summary>Several people at the register at once: row locks serialize them and the drawer never goes below zero.</summary>
public sealed class CashConcurrencyTests : CashTestBase
{
    [Fact]
    public async Task EightWithdrawalsOfThirtyFromAHundred_SucceedExactlyThreeTimes()
    {
        var id = await OpenAsync(100m);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ManualAsync(CashDirection.Out, 30m))));

        results.Count(r => r.IsSuccess).ShouldBe(3);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == CashErrors.InsufficientCash);
        (await ShiftAsync(id)).CurrentBalance.ShouldBe(10m);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task ParallelDepositsAndWithdrawals_KeepTheLedgerExact()
    {
        await OpenAsync(500m);

        var tasks = Enumerable.Range(0, 10).SelectMany(i => new[]
        {
            Task.Run(() => ManualAsync(CashDirection.In, 10m + i)),
            Task.Run(() => ManualAsync(CashDirection.Out, 5m)),
        });
        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(r => r.IsSuccess);
        (await MovementsAsync()).Count.ShouldBe(20);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task TwoPeopleOpeningAShiftAtTheSameMoment_OnlyOneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Task.Run(() => Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), 0m, null))))));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        // The loser either sees the open shift or is turned back by the unique index (a retryable conflict).
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == CashErrors.ShiftAlreadyOpen || r.Error.Code == Application.Common.CommonErrors.ConcurrencyConflict);
    }

    [Fact]
    public async Task AWithdrawalRacingTheClose_NeverLeavesAMovementOnAClosedShift()
    {
        var id = await OpenAsync(100m);

        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () => (Domain.Common.Result)await ManualAsync(CashDirection.In, 1m)))
            .Append(Task.Run(async () => (Domain.Common.Result)await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 100m, "counted early")))));
        await Task.WhenAll(tasks);

        var shift = await ShiftAsync(id);
        shift.IsOpen.ShouldBeFalse();
        // Whatever got in before the close is in the expected cash; nothing was added afterwards.
        shift.CurrentBalance.ShouldBe(100m + (await MovementsAsync()).Count);
        await AssertLedgerMatchesShiftsAsync();
    }
}
