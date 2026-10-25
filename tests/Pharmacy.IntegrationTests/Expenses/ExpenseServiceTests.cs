using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Application.Expenses;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.IntegrationTests.Expenses;

public sealed class ExpenseServiceTests : ExpensesTestBase
{
    [Fact]
    public async Task RecordingAnExpense_TakesTheCashFromTheDrawer_AndIsAudited()
    {
        await OpenAsync(1000m);

        var id = await RecordAsync(300m);

        var expense = (await ExpensesInDbAsync()).Single();
        expense.Id.ShouldBe(id);
        expense.Number.ShouldBe($"EX-{DateTime.Now.Year}-000001");
        expense.Amount.ShouldBe(300m);
        expense.Description.ShouldBe("monthly rent");
        expense.IsReversed.ShouldBeFalse();
        (await DrawerAsync()).ShouldBe(700m);
        var cash = (await MovementsAsync()).Single();
        cash.Type.ShouldBe(CashMovementType.ExpensePayment);
        cash.Amount.ShouldBe(-300m);
        cash.ReferenceId.ShouldBe(id);
        (await AuditActionsAsync()).ShouldContain(AuditActions.ExpenseRecorded);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task AnExpense_WithoutAnOpenShift_IsRejectedAndNothingIsSaved()
    {
        var result = await Expenses(s => s.RecordAsync(Spend(50m)));

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await ExpensesInDbAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnExpense_BiggerThanTheDrawer_IsRejected()
    {
        await OpenAsync(100m);

        var result = await Expenses(s => s.RecordAsync(Spend(100.01m)));

        ShouldFailWith(result, CashErrors.InsufficientCash);
        (await ExpensesInDbAsync()).ShouldBeEmpty();
        (await DrawerAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task APastDate_IsAllowed_AFutureDateIsNot()
    {
        await OpenAsync(1000m);

        var past = await Expenses(s => s.RecordAsync(Spend(10m, date: Today.AddDays(-20))));
        var future = await Expenses(s => s.RecordAsync(Spend(10m, date: Today.AddDays(1))));

        past.IsSuccess.ShouldBeTrue(past.Error?.Code);
        ShouldFailWith(future, ExpensesErrors.DateInFuture);
        (await ExpensesInDbAsync()).Single().ExpenseDate.ShouldBe(Today.AddDays(-20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    public async Task InvalidAmounts_AreRejected(double amount)
    {
        await OpenAsync(1000m);

        var result = await Expenses(s => s.RecordAsync(Spend((decimal)amount)));

        ShouldFailWith(result, ExpensesErrors.InvalidAmount);
    }

    [Fact]
    public async Task TheDescription_IsRequired()
    {
        await OpenAsync(1000m);

        var result = await Expenses(s => s.RecordAsync(Spend(10m, description: "  ")));

        ShouldFailWith(result, ExpensesErrors.DescriptionRequired);
    }

    [Fact]
    public async Task AnUnknownOrInactiveCategory_IsRejected()
    {
        await OpenAsync(1000m);
        await Categories(s => s.UpdateAsync(ElectricityCategory, "كهرباء", "Electricity", isActive: false));

        ShouldFailWith(await Expenses(s => s.RecordAsync(Spend(10m, category: 999))), ExpensesErrors.UnknownCategory);
        ShouldFailWith(await Expenses(s => s.RecordAsync(Spend(10m, category: ElectricityCategory))), ExpensesErrors.InactiveCategory);
    }

    [Fact]
    public async Task ARepeatedSubmission_RecordsTheExpenseOnce()
    {
        await OpenAsync(1000m);
        var request = Spend(100m);

        var first = await Expenses(s => s.RecordAsync(request));
        var second = await Expenses(s => s.RecordAsync(request));

        second.Value.ShouldBe(first.Value);
        (await ExpensesInDbAsync()).Count.ShouldBe(1);
        (await DrawerAsync()).ShouldBe(900m);
    }

    [Fact]
    public async Task Recording_WithoutPermission_IsForbidden()
    {
        await OpenAsync(1000m);
        SignIn(7, "viewer", Permissions.ExpensesView, Permissions.CashView);

        ShouldFailWith(await Expenses(s => s.RecordAsync(Spend(10m))), CommonErrors.Forbidden);
    }

    [Fact]
    public async Task ReversingAnExpense_PutsTheCashBack_Once()
    {
        await OpenAsync(1000m);
        var id = await RecordAsync(300m);
        var operationId = Guid.NewGuid();

        var reversed = await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(operationId, id, "entered twice")));
        var repeated = await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(operationId, id, "entered twice")));
        var again = await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), id, "other")));

        reversed.IsSuccess.ShouldBeTrue(reversed.Error?.Code);
        repeated.IsSuccess.ShouldBeTrue();
        ShouldFailWith(again, ExpensesErrors.AlreadyReversed);
        var expense = (await ExpensesInDbAsync()).Single();
        expense.IsReversed.ShouldBeTrue();
        expense.ReverseReason.ShouldBe("entered twice");
        (await DrawerAsync()).ShouldBe(1000m);
        (await MovementsAsync()).Select(m => m.Type).ShouldBe([CashMovementType.ExpensePayment, CashMovementType.ExpenseReversal]);
        (await AuditActionsAsync()).ShouldContain(AuditActions.ExpenseReversed);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task ReversingAnExpense_NeedsAReason_AndAnOpenShift()
    {
        var shift = await OpenAsync(1000m);
        var id = await RecordAsync(300m);

        var noReason = await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), id, " ")));
        await Services.InScopeAsync(s => s.GetRequiredService<CashService>()
            .CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), shift, 700m, null)));
        var closed = await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), id, "mistake")));

        ShouldFailWith(noReason, ExpensesErrors.ReasonRequired);
        ShouldFailWith(closed, CashErrors.NoOpenShift);
        (await ExpensesInDbAsync()).Single().IsReversed.ShouldBeFalse();
    }

    [Fact]
    public async Task Reversing_UnknownExpense_OrWithoutPermission_IsRejected()
    {
        await OpenAsync(1000m);
        var id = await RecordAsync(10m);

        ShouldFailWith(await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), 9999, "x"))), ExpensesErrors.UnknownExpense);
        SignIn(7, "creator", Permissions.ExpensesView, Permissions.ExpensesCreate);
        ShouldFailWith(await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), id, "x"))), CommonErrors.Forbidden);
    }
}
