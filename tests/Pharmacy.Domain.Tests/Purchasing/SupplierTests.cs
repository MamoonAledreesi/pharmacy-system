using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Domain.Tests.Purchasing;

public class SupplierTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly AccountContext _context = new(_now, UserId: 1, "accountant");

    private static Supplier NewSupplier() => Supplier.Create("Al-Noor Pharma", "0100", null, null).Value;

    [Fact]
    public void Create_TrimsTextAndBuildsTheSearchText()
    {
        var supplier = Supplier.Create("  شركة الأمل  ", " 0100 ", "  ", null).Value;

        supplier.Name.ShouldBe("شركة الأمل");
        supplier.Phone.ShouldBe("0100");
        supplier.Address.ShouldBeNull();
        supplier.SearchText.ShouldBe("شركه الامل 0100");
    }

    [Theory]
    [InlineData("", PurchasingErrors.SupplierNameRequired)]
    [InlineData("   ", PurchasingErrors.SupplierNameRequired)]
    public void Create_RequiresAName(string name, string code) =>
        Supplier.Create(name, null, null, null).Error!.Code.ShouldBe(code);

    [Fact]
    public void Create_RejectsATooLongName() =>
        Supplier.Create(new string('a', Supplier.NameMaxLength + 1), null, null, null).Error!.Code.ShouldBe(PurchasingErrors.SupplierNameTooLong);

    [Fact]
    public void IncreaseAndDecrease_MoveTheBalanceAndWriteTheRunningBalance()
    {
        var supplier = NewSupplier();

        var invoice = supplier.Increase(SupplierEntryType.Invoice, 250m, _context).Value;
        var payment = supplier.Decrease(SupplierEntryType.Payment, 100m, _context).Value;

        invoice.Amount.ShouldBe(250m);
        invoice.BalanceAfter.ShouldBe(250m);
        payment.Amount.ShouldBe(-100m);
        payment.BalanceAfter.ShouldBe(150m);
        supplier.CurrentBalance.ShouldBe(150m);
    }

    [Fact]
    public void Decrease_MoreThanOwed_IsRejectedAndChangesNothing()
    {
        var supplier = NewSupplier();
        supplier.Increase(SupplierEntryType.Invoice, 100m, _context);

        var result = supplier.Decrease(SupplierEntryType.Payment, 100.01m, _context);

        result.Error!.Code.ShouldBe(PurchasingErrors.PaymentExceedsBalance);
        supplier.CurrentBalance.ShouldBe(100m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    [InlineData(2_000_000_000)]
    public void Increase_RejectsInvalidAmounts(double amount) =>
        NewSupplier().Increase(SupplierEntryType.Invoice, (decimal)amount, _context).Error!.Code.ShouldBe(PurchasingErrors.InvalidAmount);

    [Fact]
    public void WrongEntryTypes_AreProgrammingErrors()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewSupplier().Increase(SupplierEntryType.Payment, 1m, _context));
        Should.Throw<ArgumentOutOfRangeException>(() => NewSupplier().Decrease(SupplierEntryType.Invoice, 1m, _context));
    }

    [Fact]
    public void Payment_CanBeReversedOnlyOnce()
    {
        var payment = SupplierPayment.Create(1, 50m, null, Guid.NewGuid(), _now, 1, "accountant").Value;

        var first = payment.Reverse("typed twice", Guid.NewGuid(), _now, "manager");
        var second = payment.Reverse("again", Guid.NewGuid(), _now, "manager");

        first.IsSuccess.ShouldBeTrue();
        second.Error!.Code.ShouldBe(PurchasingErrors.PaymentAlreadyReversed);
        payment.IsReversed.ShouldBeTrue();
        payment.ReverseReason.ShouldBe("typed twice");
    }

    [Fact]
    public void Reversal_NeedsAReason()
    {
        var payment = SupplierPayment.Create(1, 50m, null, Guid.NewGuid(), _now, 1, "accountant").Value;

        payment.Reverse(" ", Guid.NewGuid(), _now, "manager").Error!.Code.ShouldBe(PurchasingErrors.ReasonRequired);
        payment.IsReversed.ShouldBeFalse();
    }
}

public class PurchaseInvoiceTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _today = new(2026, 10, 24);

    private static NewPurchaseLine Line(int quantity = 2, decimal cost = 120m) =>
        new(ProductId: 1, ProductUnitId: 2, quantity, UnitFactor: 3, BaseQuantity: quantity * 3, cost, BaseUnitCost: cost / 3, "B1", new DateOnly(2028, 1, 31));

    private static Result<PurchaseInvoice> Create(
        IReadOnlyList<NewPurchaseLine> lines, decimal paidCash = 0m, int? supplierId = 5, DateOnly? date = null) =>
        PurchaseInvoice.Create("PI-2026-000001", supplierId, null, date ?? _today, _today, "77", paidCash, null, Guid.NewGuid(), _now, 1, "keeper", lines);

    [Fact]
    public void Totals_AreTheSumOfTheRoundedLineTotals()
    {
        var invoice = Create([Line(2, 120m), Line(3, 10.555m)], paidCash: 100m).Value;

        invoice.Lines.Select(l => l.LineTotal).ShouldBe([240m, 31.67m]);
        invoice.Total.ShouldBe(271.67m);
        invoice.PaidCash.ShouldBe(100m);
        invoice.OnCredit.ShouldBe(171.67m);
    }

    [Fact]
    public void FullyPaidInvoice_OwesNothing_AndNeedsNoSupplier()
    {
        var invoice = Create([Line()], paidCash: 240m, supplierId: null).Value;

        invoice.OnCredit.ShouldBe(0m);
        invoice.SupplierId.ShouldBeNull();
    }

    [Fact]
    public void Credit_NeedsARegisteredSupplier() =>
        Create([Line()], paidCash: 100m, supplierId: null).Error!.Code.ShouldBe(PurchasingErrors.SupplierRequiredForCredit);

    [Theory]
    [InlineData(-1)]
    [InlineData(240.01)]
    [InlineData(10.005)]
    public void PaidCash_MustBeBetweenZeroAndTheTotal(double paid) =>
        Create([Line()], paidCash: (decimal)paid).Error!.Code.ShouldBe(PurchasingErrors.InvalidPaidAmount);

    [Fact]
    public void Lines_AreRequired() =>
        Create([]).Error!.Code.ShouldBe(PurchasingErrors.NoLines);

    [Fact]
    public void FutureDate_IsRejected() =>
        Create([Line()], date: _today.AddDays(1)).Error!.Code.ShouldBe(PurchasingErrors.InvoiceDateInFuture);

    [Fact]
    public void SupplierName_IsKeptOnlyForUnregisteredSellers()
    {
        var registered = PurchaseInvoice.Create("N", 5, "Someone", _today, _today, null, 0m, null, Guid.NewGuid(), _now, 1, "u", [Line()]).Value;
        var walkIn = PurchaseInvoice.Create("N", null, "  Someone  ", _today, _today, null, 240m, null, Guid.NewGuid(), _now, 1, "u", [Line()]).Value;

        registered.SupplierName.ShouldBeNull();
        walkIn.SupplierName.ShouldBe("Someone");
    }
}

public class DocumentSequenceTests
{
    [Fact]
    public void Next_FormatsPrefixYearAndASixDigitCounter()
    {
        var sequence = new DocumentSequence("PI", 2026);

        sequence.Next().ShouldBe("PI-2026-000001");
        sequence.Next().ShouldBe("PI-2026-000002");
        sequence.LastNumber.ShouldBe(2);
    }
}
