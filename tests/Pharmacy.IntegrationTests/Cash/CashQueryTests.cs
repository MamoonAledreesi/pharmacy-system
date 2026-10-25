using Pharmacy.Application.Cash;
using Pharmacy.Domain.Cash;

namespace Pharmacy.IntegrationTests.Cash;

public sealed class CashQueryTests : CashTestBase
{
    [Fact]
    public async Task RegisterState_WithNoShift_SuggestsZero()
    {
        var state = (await Query(s => s.GetRegisterStateAsync())).Value;

        state.Current.ShouldBeNull();
        state.SuggestedOpeningBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task RegisterState_SuggestsTheLastCountedCash_AndShowsTheOpenShift()
    {
        var first = await OpenAsync(100m);
        await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), first, 100m, null)));
        var afterClose = (await Query(s => s.GetRegisterStateAsync())).Value;

        await OpenAsync(100m, note: null);
        await ManualAsync(CashDirection.In, 40m);
        var open = (await Query(s => s.GetRegisterStateAsync())).Value;

        afterClose.SuggestedOpeningBalance.ShouldBe(100m);
        open.Current.ShouldNotBeNull();
        open.Current!.CurrentBalance.ShouldBe(140m);
        open.Current.TotalIn.ShouldBe(40m);
        open.Current.TotalOut.ShouldBe(0m);
        open.Current.IsStale.ShouldBeFalse();
    }

    [Fact]
    public async Task Movements_ArePagedNewestFirst()
    {
        var id = await OpenAsync(100m);
        for (var i = 1; i <= 5; i++)
        {
            await ManualAsync(CashDirection.In, i);
        }

        var page = (await Query(s => s.SearchMovementsAsync(new CashMovementQuery(id, null, 0, 2)))).Value;

        page.TotalCount.ShouldBe(5);
        page.Items.Select(m => m.Amount).ShouldBe([5m, 4m]);
    }

    [Fact]
    public async Task Shifts_AreListedNewestFirst()
    {
        var first = await OpenAsync(100m);
        await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), first, 100m, null)));
        var second = await OpenAsync(100m, note: null);

        var page = (await Query(s => s.SearchShiftsAsync(new ShiftQuery(0, 25)))).Value;

        page.Items.Select(s => s.Id).ShouldBe([second, first]);
    }

    [Fact]
    public async Task DailySummary_TotalsTheLocalDaysMovementsByType()
    {
        await OpenAsync(100m);
        await ManualAsync(CashDirection.In, 50m);
        await ManualAsync(CashDirection.In, 25m);
        await ManualAsync(CashDirection.Out, 10m);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var summary = (await Query(s => s.GetDailySummaryAsync(today))).Value;
        var yesterday = (await Query(s => s.GetDailySummaryAsync(today.AddDays(-1)))).Value;

        summary.TotalIn.ShouldBe(75m);
        summary.TotalOut.ShouldBe(10m);
        summary.Net.ShouldBe(65m);
        summary.ByType.Single(t => t.Type == CashMovementType.ManualIn).Count.ShouldBe(2);
        summary.Shifts.Count.ShouldBe(1);
        yesterday.TotalIn.ShouldBe(0m);
        yesterday.Shifts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Queries_WithoutViewPermission_AreForbidden()
    {
        SignIn(8, "nobody");

        var state = await Query(s => s.GetRegisterStateAsync());

        state.IsFailure.ShouldBeTrue();
    }
}
