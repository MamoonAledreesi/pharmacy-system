using Pharmacy.Domain.Receivables;

namespace Pharmacy.Domain.Tests.Receivables;

public class CustomerTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly CustomerAccountContext _context = new(_now, UserId: 1, "cashier");

    private static Customer NewCustomer() => Customer.Create("Ahmed Ali", "0100", null, null).Value;

    [Fact]
    public void Create_TrimsTextAndBuildsTheSearchText()
    {
        var customer = Customer.Create("  أحمد علي  ", " 0100 ", "  ", null).Value;

        customer.Name.ShouldBe("أحمد علي");
        customer.Phone.ShouldBe("0100");
        customer.Address.ShouldBeNull();
        customer.SearchText.ShouldBe("احمد علي 0100");
    }

    [Fact]
    public void Create_RequiresAName() =>
        Customer.Create("  ", null, null, null).Error!.Code.ShouldBe(ReceivablesErrors.CustomerNameRequired);

    [Fact]
    public void Create_RejectsATooLongName() =>
        Customer.Create(new string('a', Customer.NameMaxLength + 1), null, null, null).Error!.Code.ShouldBe(ReceivablesErrors.CustomerNameTooLong);

    [Fact]
    public void DebitAndCredit_MoveTheBalanceAndWriteTheRunningBalance()
    {
        var customer = NewCustomer();

        var sale = customer.Debit(CustomerEntryType.Sale, 250m, _context).Value;
        var collection = customer.Credit(CustomerEntryType.Collection, 100m, _context).Value;

        sale.Amount.ShouldBe(250m);
        sale.BalanceAfter.ShouldBe(250m);
        collection.Amount.ShouldBe(-100m);
        collection.BalanceAfter.ShouldBe(150m);
        customer.CurrentBalance.ShouldBe(150m);
    }

    [Fact]
    public void Credit_MoreThanOwed_IsRejectedAndChangesNothing()
    {
        var customer = NewCustomer();
        customer.Debit(CustomerEntryType.Sale, 100m, _context);

        var result = customer.Credit(CustomerEntryType.Collection, 100.01m, _context);

        result.Error!.Code.ShouldBe(ReceivablesErrors.CollectionExceedsBalance);
        customer.CurrentBalance.ShouldBe(100m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    [InlineData(2_000_000_000)]
    public void Debit_RejectsInvalidAmounts(double amount) =>
        NewCustomer().Debit(CustomerEntryType.Sale, (decimal)amount, _context).Error!.Code.ShouldBe(ReceivablesErrors.InvalidAmount);

    [Fact]
    public void WrongEntryTypes_AreProgrammingErrors()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewCustomer().Debit(CustomerEntryType.Collection, 1m, _context));
        Should.Throw<ArgumentOutOfRangeException>(() => NewCustomer().Credit(CustomerEntryType.Sale, 1m, _context));
    }

    [Fact]
    public void Collection_CanBeReversedOnlyOnce()
    {
        var collection = DebtCollection.Create("RC-2026-000001", 1, 50m, null, Guid.NewGuid(), _now, 1, "cashier").Value;

        var first = collection.Reverse("wrong customer", Guid.NewGuid(), _now, "manager");
        var second = collection.Reverse("again", Guid.NewGuid(), _now, "manager");

        first.IsSuccess.ShouldBeTrue();
        second.Error!.Code.ShouldBe(ReceivablesErrors.CollectionAlreadyReversed);
        collection.IsReversed.ShouldBeTrue();
    }

    [Fact]
    public void Reversal_NeedsAReason()
    {
        var collection = DebtCollection.Create("RC-2026-000001", 1, 50m, null, Guid.NewGuid(), _now, 1, "cashier").Value;

        collection.Reverse(" ", Guid.NewGuid(), _now, "manager").Error!.Code.ShouldBe(ReceivablesErrors.ReasonRequired);
        collection.IsReversed.ShouldBeFalse();
    }
}
