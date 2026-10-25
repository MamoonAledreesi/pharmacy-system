using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Sales;

namespace Pharmacy.IntegrationTests.Sales;

public sealed class SalesReturnQueryTests : ReturnsTestBase
{
    [Fact]
    public async Task Returnable_ShowsWhatWasSoldReturnedAndIsLeft_PerLine()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(5, 15m)], paid: 75m);
        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 2)])));

        var returnable = (await ReturnsQuery(s => s.GetReturnableAsync(invoice))).Value;

        var item = returnable.Lines.Single();
        item.SoldBaseQuantity.ShouldBe(5);
        item.ReturnedBaseQuantity.ShouldBe(2);
        item.RemainingBaseQuantity.ShouldBe(3);
        item.NetTotal.ShouldBe(75m);
        item.Units.Select(u => u.Factor).ShouldBe([1, 3]);
        returnable.IsLate.ShouldBeFalse();
        returnable.WindowDays.ShouldBe(14);
        returnable.CanReturnLate.ShouldBeFalse();
        returnable.IsDefaultCash.ShouldBeTrue();
    }

    [Fact]
    public async Task Returnable_FlagsAnInvoiceOlderThanTheReturnPeriod()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(1, 15m)], paid: 15m);
        await AgeInvoiceAsync(invoice, 15);
        SignIn(LateReturner);

        var returnable = (await ReturnsQuery(s => s.GetReturnableAsync(invoice))).Value;

        returnable.IsLate.ShouldBeTrue();
        returnable.DaysSinceSale.ShouldBe(15);
        returnable.CanReturnLate.ShouldBeTrue();
    }

    [Fact]
    public async Task Search_FiltersByNumberInvoiceCustomerAndDate_NewestFirst()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var customer = await CreateCustomerAsync("Sara Mohamed");
        var cashInvoice = await SellAsync([Strips(2, 15m)], paid: 30m);
        var creditInvoice = (await Sales(s => s.CreateAsync(Sale([Strips(2, 15m)], customerId: customer, paid: 0m)))).Value;
        var cashLine = await LineIdAsync(cashInvoice);
        var creditLine = await LineIdAsync(creditInvoice);
        var first = (await Returns(s => s.CreateAsync(Return(cashInvoice, [Back(cashLine, 1)])))).Value;
        var second = (await Returns(s => s.CreateAsync(Return(creditInvoice, [Back(creditLine, 1)])))).Value;

        var all = (await ReturnsQuery(s => s.SearchAsync(new ReturnQuery(null, null, null, null, 0, 25)))).Value;
        var byCustomer = (await ReturnsQuery(s => s.SearchAsync(new ReturnQuery("Sara", null, null, null, 0, 25)))).Value;
        var byInvoice = (await ReturnsQuery(s => s.SearchAsync(new ReturnQuery(null, cashInvoice, null, null, 0, 25)))).Value;
        var yesterday = (await ReturnsQuery(s => s.SearchAsync(new ReturnQuery(null, null, Today.AddDays(-3), Today.AddDays(-1), 0, 25)))).Value;

        all.Items.Select(i => i.Id).ShouldBe([second, first]);
        byCustomer.Items.Select(i => i.Id).ShouldBe([second]);
        byInvoice.Items.Select(i => i.Id).ShouldBe([first]);
        yesterday.TotalCount.ShouldBe(0);
        all.Items.First().DebtReduction.ShouldBe(15m);
    }

    [Fact]
    public async Task Details_ShowTheLinesTheirValueAndWhereTheGoodsWent()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(4, 15m)], paid: 60m);
        var line = await LineIdAsync(invoice);
        var id = (await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3, sellable: false)], reason: "damaged box")))).Value;

        var detail = (await ReturnsQuery(s => s.GetAsync(id))).Value;

        detail.Total.ShouldBe(45m);
        detail.Reason.ShouldBe("damaged box");
        detail.CustomerName.ShouldContain("Cash");
        var item = detail.Lines.Single();
        item.Product.English.ShouldBe("Panadol");
        item.Quantity.ShouldBe(3);
        item.AnyQuarantined.ShouldBeTrue();
        item.AnySellable.ShouldBeFalse();
    }

    [Fact]
    public async Task Queries_WithoutPermission_AreForbidden()
    {
        SignIn();

        (await ReturnsQuery(s => s.SearchAsync(new ReturnQuery(null, null, null, null, 0, 25)))).IsFailure.ShouldBeTrue();
        (await ReturnsQuery(s => s.GetAsync(1))).IsFailure.ShouldBeTrue();
        SignIn(Permissions.SalesView);
        var returnable = await ReturnsQuery(s => s.GetReturnableAsync(1));
        returnable.Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }
}
