using Pharmacy.Application.Common;
using Pharmacy.Application.Expenses;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.IntegrationTests.Expenses;

/// <summary>Several people spending from one drawer at once: the shift lock makes them take turns.</summary>
public sealed class ExpenseConcurrencyTests : ExpensesTestBase
{
    [Fact]
    public async Task SevenExpensesOfThirtyAgainstADrawerOfHundred_SucceedExactlyThreeTimes_NumberedWithoutGaps()
    {
        await OpenAsync(130m);
        // The first expense creates the number sequence; racing it is retried by the user, so start it alone.
        await RecordAsync(30m);

        var results = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ =>
            Task.Run(() => Expenses(s => s.RecordAsync(Spend(30m))))));

        results.Count(r => r.IsSuccess).ShouldBe(3);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == CashErrors.InsufficientCash);
        (await DrawerAsync()).ShouldBe(10m);
        (await ExpensesInDbAsync()).Select(e => e.Number).Order().ShouldBe(
            Enumerable.Range(1, 4).Select(n => $"EX-{DateTime.Now.Year}-{n:D6}").ToList());
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task FiveReversalsOfTheSameExpense_PutTheCashBackOnlyOnce()
    {
        await OpenAsync(1000m);
        var id = await RecordAsync(300m);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Task.Run(() => Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), id, "typo"))))));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => r.IsFailure).ShouldAllBe(r =>
            r.Error!.Code == ExpensesErrors.AlreadyReversed || r.Error.Code == CommonErrors.ConcurrencyConflict);
        (await DrawerAsync()).ShouldBe(1000m);
        (await MovementsAsync()).Count(m => m.Type == CashMovementType.ExpenseReversal).ShouldBe(1);
    }
}
