using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Domain.Tests.Sales;

public class SalePricingTests
{
    private static PricedLineInput Line(int quantity, decimal price) => new(quantity, price);

    [Fact]
    public void WithoutADiscount_TheNetIsTheSubtotal()
    {
        var priced = SalePricing.Price([Line(2, 10m), Line(1, 5.5m)], InvoiceDiscount.None).Value;

        priced.LineSubtotals.ShouldBe([20m, 5.5m]);
        priced.Subtotal.ShouldBe(25.5m);
        priced.Discount.ShouldBe(0m);
        priced.Net.ShouldBe(25.5m);
        priced.DiscountShares.ShouldAllBe(s => s == 0m);
    }

    [Fact]
    public void ADiscountByPercent_IsRoundedToTheCent()
    {
        var priced = SalePricing.Price([Line(1, 33.33m)], new InvoiceDiscount(null, 10m)).Value;

        priced.Discount.ShouldBe(3.33m);
        priced.Net.ShouldBe(30m);
        priced.DiscountPercent.ShouldBe(9.99m);
    }

    [Theory]
    [InlineData(1.00, new[] { 0.34, 0.33, 0.33 })]
    [InlineData(0.01, new[] { 0.01, 0.00, 0.00 })]
    [InlineData(2.00, new[] { 0.66, 0.67, 0.67 })]
    public void TheDiscount_IsSpreadInProportion_AndTheSharesAddUpExactly(double discount, double[] expected)
    {
        var priced = SalePricing.Price([Line(1, 10m), Line(1, 10m), Line(1, 10m)], new InvoiceDiscount((decimal)discount, null)).Value;

        priced.DiscountShares.Sum().ShouldBe((decimal)discount);
        priced.DiscountShares.Select(s => (double)s).ShouldBe(expected);
        priced.LineNets.Sum().ShouldBe(priced.Net);
    }

    [Fact]
    public void TheSharesFollowTheLineSizes()
    {
        var priced = SalePricing.Price([Line(1, 90m), Line(1, 10m)], new InvoiceDiscount(10m, null)).Value;

        priced.DiscountShares.ShouldBe([9m, 1m]);
        priced.LineNets.ShouldBe([81m, 9m]);
    }

    [Fact]
    public void TheRoundingDifference_GoesToTheLargestLine()
    {
        var priced = SalePricing.Price([Line(1, 1m), Line(1, 1m), Line(1, 5m)], new InvoiceDiscount(0.50m, null)).Value;

        priced.DiscountShares.Sum().ShouldBe(0.50m);
        priced.DiscountShares[2].ShouldBeGreaterThanOrEqualTo(priced.DiscountShares[0]);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(25)]
    public void ADiscountOfTheWholeSubtotalOrMore_IsRejected(double amount) =>
        SalePricing.Price([Line(1, 20m)], new InvoiceDiscount((decimal)amount, null)).Error!.Code.ShouldBe(SalesErrors.DiscountTooLarge);

    [Fact]
    public void AHundredPercentDiscount_IsRejected() =>
        SalePricing.Price([Line(1, 20m)], new InvoiceDiscount(null, 100m)).Error!.Code.ShouldBe(SalesErrors.DiscountTooLarge);

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(1.234, null)]
    [InlineData(null, -5.0)]
    [InlineData(null, 101.0)]
    public void AnInvalidDiscount_IsRejected(double? amount, double? percent) =>
        SalePricing.Price([Line(1, 20m)], new InvoiceDiscount((decimal?)amount, (decimal?)percent)).Error!.Code.ShouldBe(SalesErrors.InvalidDiscount);

    [Fact]
    public void AnAmountAndAPercentTogether_AreRejected() =>
        SalePricing.Price([Line(1, 20m)], new InvoiceDiscount(1m, 5m)).Error!.Code.ShouldBe(SalesErrors.InvalidDiscount);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -3)]
    [InlineData(1, 10.00001)]
    public void InvalidQuantitiesAndPrices_AreRejected(int quantity, double price) =>
        SalePricing.Price([Line(quantity, (decimal)price)], InvoiceDiscount.None).Error!.Code.ShouldBe(SalesErrors.InvalidPrice);

    [Fact]
    public void NoLines_AreRejected() =>
        SalePricing.Price([], InvoiceDiscount.None).Error!.Code.ShouldBe(SalesErrors.NoLines);

    [Fact]
    public void ATotalAboveTheMaximum_IsRejected() =>
        SalePricing.Price([Line(2, 600_000_000m)], InvoiceDiscount.None).Error!.Code.ShouldBe(SalesErrors.TotalTooLarge);
}

public class SalesInvoiceTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

    private static NewSaleLine Strip(int quantity = 3, decimal price = 15m) =>
        new(ProductId: 1, ProductUnitId: 1, UnitFactor: 1, quantity, BaseQuantity: quantity, price);

    private static Pharmacy.Domain.Common.Result<SalesInvoice> Create(decimal paid, decimal discount = 0m, params NewSaleLine[] lines)
    {
        lines = lines.Length == 0 ? [Strip()] : lines;
        var priced = SalePricing.Price(
            lines.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice)).ToList(), new InvoiceDiscount(discount == 0 ? null : discount, null)).Value;
        return SalesInvoice.Create("SI-2026-000001", 1, priced, lines, paid, null, Guid.NewGuid(), _now, 1, "cashier");
    }

    [Fact]
    public void TheAmountOwed_IsTheTotalMinusTheCashPaid()
    {
        var invoice = Create(paid: 20m).Value;

        invoice.Total.ShouldBe(45m);
        invoice.OnCredit.ShouldBe(25m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(45.01)]
    [InlineData(10.005)]
    public void PaidCash_MustBeBetweenZeroAndTheTotal(double paid) =>
        Create((decimal)paid).Error!.Code.ShouldBe(SalesErrors.InvalidPaidAmount);

    [Fact]
    public void Allocations_SetTheCostOfTheLine_AndFlagBelowCost()
    {
        var invoice = Create(paid: 45m).Value;

        invoice.RecordAllocations(0, [new StockAllocation(1, 2, 10m, null, false), new StockAllocation(2, 1, 12.5m, null, false)]);

        invoice.Lines.Single().CostTotal.ShouldBe(32.5m);
        invoice.Lines.Single().IsBelowCost.ShouldBeFalse();
        invoice.HasBelowCostLines.ShouldBeFalse();

        invoice.RecordAllocations(0, [new StockAllocation(1, 3, 16m, null, false)]);

        invoice.Lines.Single().CostTotal.ShouldBe(48m);
        invoice.Lines.Single().IsBelowCost.ShouldBeTrue();
        invoice.HasBelowCostLines.ShouldBeTrue();
    }

    [Fact]
    public void ADiscount_CanTurnALineIntoABelowCostLine()
    {
        var invoice = Create(paid: 40.5m, discount: 4.5m).Value;

        invoice.RecordAllocations(0, [new StockAllocation(1, 3, 14m, null, false)]);

        invoice.Lines.Single().NetTotal.ShouldBe(40.5m);
        invoice.HasBelowCostLines.ShouldBeTrue();
    }

    [Fact]
    public void Approvals_AreRecorded()
    {
        var invoice = Create(paid: 45m).Value;

        invoice.RecordApprovals("pharm1", "  clearance  ", "pharm2").IsSuccess.ShouldBeTrue();

        invoice.BelowCostApprovedBy.ShouldBe("pharm1");
        invoice.BelowCostReason.ShouldBe("clearance");
        invoice.DiscountApprovedBy.ShouldBe("pharm2");
    }
}

public class DiscountSettingTests
{
    [Fact]
    public void TheDefaultLimit_IsTenPercent() =>
        Pharmacy.Domain.Settings.PharmacySettings.CreateDefault().MaxDiscountPercent.ShouldBe(10m);

    [Theory]
    [InlineData(0)]
    [InlineData(25.5)]
    [InlineData(100)]
    public void AValidLimit_IsStored(double percent)
    {
        var settings = Pharmacy.Domain.Settings.PharmacySettings.CreateDefault();

        settings.Update(Pharmacy.Domain.Settings.PharmacySettings.Defaults with { MaxDiscountPercent = (decimal)percent }).IsSuccess.ShouldBeTrue();

        settings.MaxDiscountPercent.ShouldBe((decimal)percent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(5.555)]
    public void AnInvalidLimit_IsRejectedAndNothingChanges(double percent)
    {
        var settings = Pharmacy.Domain.Settings.PharmacySettings.CreateDefault();

        var result = settings.Update(Pharmacy.Domain.Settings.PharmacySettings.Defaults with { NameEnglish = "Changed", MaxDiscountPercent = (decimal)percent });

        result.Error!.Code.ShouldBe(Pharmacy.Domain.Settings.SettingsErrors.InvalidMaxDiscountPercent);
        settings.MaxDiscountPercent.ShouldBe(10m);
        settings.NameEnglish.ShouldNotBe("Changed");
    }
}
