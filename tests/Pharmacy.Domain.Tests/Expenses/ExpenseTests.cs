using Pharmacy.Domain.Common;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.Domain.Tests.Expenses;

public class ExpenseTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _today = new(2026, 10, 24);

    private static Result<Expense> Create(
        decimal amount = 100m, DateOnly? date = null, string? description = "June rent", string? payee = null) =>
        Expense.Create("EX-2026-000001", 1, amount, date ?? _today, _today, description, payee, Guid.NewGuid(), _now, 1, "accountant");

    [Fact]
    public void Create_TrimsTextAndStartsActive()
    {
        var expense = Create(description: "  June rent  ", payee: "  Mr Ahmed ").Value;

        expense.Description.ShouldBe("June rent");
        expense.Payee.ShouldBe("Mr Ahmed");
        expense.IsReversed.ShouldBeFalse();
        expense.ExpenseDate.ShouldBe(_today);
    }

    [Fact]
    public void ABlankPayee_IsKeptAsNothing() =>
        Create(payee: "   ").Value.Payee.ShouldBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.005)]
    public void InvalidAmounts_AreRejected(double amount) =>
        Create((decimal)amount).Error!.Code.ShouldBe(ExpensesErrors.InvalidAmount);

    [Fact]
    public void AnAmountAboveTheMaximum_IsRejected() =>
        Create(Expense.MaxAmount + 1).Error!.Code.ShouldBe(ExpensesErrors.AmountTooLarge);

    [Fact]
    public void TheMaximumAmount_IsAccepted() =>
        Create(Expense.MaxAmount).IsSuccess.ShouldBeTrue();

    [Fact]
    public void APastDate_IsAccepted_AFutureDateIsNot()
    {
        Create(date: _today.AddDays(-90)).IsSuccess.ShouldBeTrue();
        Create(date: _today.AddDays(1)).Error!.Code.ShouldBe(ExpensesErrors.DateInFuture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheDescription_IsRequired(string? description) =>
        Create(description: description).Error!.Code.ShouldBe(ExpensesErrors.DescriptionRequired);

    [Fact]
    public void TooLongText_IsRejected()
    {
        Create(description: new string('a', Expense.DescriptionMaxLength + 1)).Error!.Code.ShouldBe(ExpensesErrors.FieldTooLong);
        Create(payee: new string('a', Expense.PayeeMaxLength + 1)).Error!.Code.ShouldBe(ExpensesErrors.FieldTooLong);
    }

    [Fact]
    public void AnExpense_CanBeReversedOnlyOnce_WithAReason()
    {
        var expense = Create().Value;

        expense.Reverse(" ", Guid.NewGuid(), _now, "manager").Error!.Code.ShouldBe(ExpensesErrors.ReasonRequired);
        expense.IsReversed.ShouldBeFalse();

        expense.Reverse("entered twice", Guid.NewGuid(), _now, "manager").IsSuccess.ShouldBeTrue();
        expense.Reverse("again", Guid.NewGuid(), _now, "manager").Error!.Code.ShouldBe(ExpensesErrors.AlreadyReversed);

        expense.IsReversed.ShouldBeTrue();
        expense.ReverseReason.ShouldBe("entered twice");
        expense.ReversedByUserName.ShouldBe("manager");
    }
}
