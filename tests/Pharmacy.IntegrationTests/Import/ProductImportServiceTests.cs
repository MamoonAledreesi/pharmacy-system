using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Common;
using Pharmacy.Application.Import;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Import;

public sealed class ProductImportServiceTests : ImportTestBase
{
    private static string[] Codes(IEnumerable<Pharmacy.Domain.Common.Error> errors) => [.. errors.Select(e => e.Code)];

    [Fact]
    public async Task ANewProductWithItsOpeningStock_IsCreatedWithUnitsAndABatch()
    {
        var result = await ImportAsync(false, null, Row(2, baseBarcode: "111", largerBarcode: "222", batch: "L1", expiry: Future, quantity: 10, quantityUnit: "Box", cost: 20m, price: 8m));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (result.Value.ProductsCreated, result.Value.ProductsExisting, result.Value.BatchesRecorded).ShouldBe((1, 0, 1));
        var product = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Products.Include(p => p.Units).SingleAsync(p => p.TradeNameEnglish == "Amoxil"));
        product.TracksExpiry.ShouldBeTrue();
        product.Units.Select(u => (u.IsBase, u.Factor, u.Barcode)).OrderBy(u => u.Factor).ShouldBe([(true, 1, "111"), (false, 4, "222")]);
        product.BaseUnit.DefaultSellingPrice.ShouldBe(8m);
        var batch = (await BatchesAsync(product.Id)).Single();
        (batch.BatchNumber, batch.QuantityAvailable, batch.UnitCost).ShouldBe(("L1", 40, 5m));   // 10 boxes of 4 strips, 20 a box
        (await AuditActionsAsync()).ShouldContain("Catalog.ProductsImported");
    }

    [Fact]
    public async Task SeveralRowsOfOneProduct_MakeOneProductWithSeveralBatches()
    {
        var result = await ImportAsync(false, null,
            Row(2, batch: "A", expiry: Future, quantity: 5, cost: 4m),
            Row(3, batch: "B", expiry: Future.AddDays(30), quantity: 7, cost: 4.5m));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (result.Value.ProductsCreated, result.Value.BatchesRecorded).ShouldBe((1, 2));
        (await CountAsync<Product>()).ShouldBe(2);   // the standard test product plus this one
        var product = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Products.SingleAsync(p => p.TradeNameEnglish == "Amoxil"));
        (await BatchesAsync(product.Id)).Select(b => (b.BatchNumber, b.QuantityAvailable)).ShouldBe([("A", 5), ("B", 7)]);
    }

    [Fact]
    public async Task AProductThatExists_IsNotChanged_ButGetsTheStockOfItsRow()
    {
        var existingId = ProductId;
        var before = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Products.AsNoTracking().SingleAsync(p => p.Id == existingId));

        var result = await ImportAsync(false, null, Row(2, ar: null, en: "Panadol", strength: null, category: "Anything", baseUnit: "Tablet", largerUnit: null, perLarger: null, batch: "X1", expiry: Future, quantity: 25, cost: 1m));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (result.Value.ProductsCreated, result.Value.ProductsExisting, result.Value.BatchesRecorded).ShouldBe((0, 1, 1));
        var after = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Products.AsNoTracking().SingleAsync(p => p.Id == existingId));
        after.SearchText.ShouldBe(before.SearchText);
        (await BatchesAsync(existingId)).Single().QuantityAvailable.ShouldBe(25);
    }

    [Fact]
    public async Task AProductKnownByItsBarcode_IsMatched_EvenWithAnotherName()
    {
        await ImportAsync(false, null, Row(2, baseBarcode: "9001")).ContinueWith(t => t.Result.IsSuccess.ShouldBeTrue());

        var preview = (await PreviewAsync(Row(2, ar: "اسم آخر", en: "Other name", baseBarcode: "9001"))).Value;

        preview.Rows.Single().ProductExists.ShouldBeTrue();
        Codes(preview.Rows.Single().Warnings).ShouldContain(ImportWarnings.MatchedByBarcode);
    }

    [Fact]
    public async Task UploadingTheSameFileAgain_NeverDoublesTheStock()
    {
        var file = Guid.NewGuid();
        Task<Pharmacy.Domain.Common.Result<ImportResult>> Run() => ImportAsync(false, file, Row(2, batch: "L1", expiry: Future, quantity: 10, cost: 5m));

        var first = await Run();
        var second = await Run();

        first.Value.ProductsCreated.ShouldBe(1);
        second.Value.ProductsCreated.ShouldBe(0);
        var product = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Products.SingleAsync(p => p.TradeNameEnglish == "Amoxil"));
        (await BatchesAsync(product.Id)).Single().QuantityAvailable.ShouldBe(10);
        (await MovementsAsync(product.Id)).Count(m => m.Type == StockMovementType.OpeningStock).ShouldBe(1);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task EveryProblemOfARow_IsReportedWithItsReason()
    {
        var preview = (await PreviewAsync(
            Row(2, category: null),
            Row(3, ar: "ب", en: "B", category: "Nope"),
            Row(4, ar: "ج", en: "C", baseUnit: null),
            Row(5, ar: "د", en: "D", baseUnit: "Strange"),
            Row(6, ar: "هـ", en: "E", perLarger: null),
            Row(7, ar: "و", en: "F", largerUnit: "Strip"),
            Row(8, ar: "ز", en: "G", form: "Weird form"),
            Row(9, ar: null, en: null),
            Row(10, ar: "ح", en: "H", batch: "L", quantity: 0, cost: 1m, expiry: Future),
            Row(11, ar: "ط", en: "I", batch: "L", quantity: 1, cost: null, expiry: Future),
            Row(12, ar: "ي", en: "J", batch: "L", quantity: 1, cost: 1m, expiry: null),
            Row(13, ar: "ك", en: "K", batch: "L", quantity: 1, cost: 1m, expiry: Future, quantityUnit: "Barrel"),
            Row(14, ar: "ل", en: "L", largerUnit: null, perLarger: null, batch: "L", quantity: 1, cost: 1m, expiry: Future, quantityUnit: "larger"),
            Row(15, ar: "م", en: "M", unreadable: ["UnitCost"], batch: "L", quantity: 1, expiry: Future))).Value;

        var reasons = preview.Rows.ToDictionary(r => r.RowNumber, r => Codes(r.Errors));

        preview.ErrorCount.ShouldBe(14);
        reasons[2].ShouldContain(ImportErrors.CategoryRequired);
        reasons[3].ShouldContain(ImportErrors.UnknownCategory);
        reasons[4].ShouldContain(ImportErrors.BaseUnitRequired);
        reasons[5].ShouldContain(ImportErrors.UnknownUnit);
        reasons[6].ShouldContain(ImportErrors.LargerUnitNeedsCount);
        reasons[7].ShouldContain(ImportErrors.LargerUnitSameAsBase);
        reasons[8].ShouldContain(ImportErrors.UnknownDosageForm);
        reasons[9].ShouldContain(CatalogErrors.TradeNameRequired);
        reasons[10].ShouldContain(ImportErrors.QuantityRequired);
        reasons[11].ShouldContain(ImportErrors.UnitCostRequired);
        reasons[12].ShouldContain(ImportErrors.ExpiryRequired);
        reasons[13].ShouldContain(ImportErrors.UnknownQuantityUnit);
        reasons[14].ShouldContain(ImportErrors.NoLargerUnit);
        reasons[15].ShouldContain(ImportErrors.Unreadable);
    }

    [Fact]
    public async Task ABarcodeUsedByTwoProductsInTheFile_IsRefused()
    {
        var preview = (await PreviewAsync(Row(2, baseBarcode: "777"), Row(3, ar: "ب", en: "B", baseBarcode: "777"))).Value;

        preview.Rows[0].Status.ShouldNotBe(ImportRowStatus.Error);
        Codes(preview.Rows[1].Errors).ShouldContain(ImportErrors.DuplicateBarcodeInFile);
    }

    [Fact]
    public async Task ARowThatContradictsItsProduct_IsRefused()
    {
        var preview = (await PreviewAsync(Row(2), Row(3, category: "General medicines", baseUnit: "Tablet"))).Value;

        Codes(preview.Rows[1].Errors).ShouldContain(ImportErrors.InconsistentProduct);
    }

    [Fact]
    public async Task WarningsDoNotStopAnImport_ButAreShown()
    {
        var preview = (await PreviewAsync(
            Row(2, batch: "L", expiry: Future.AddDays(-1000), quantity: 1, cost: 1m),
            Row(3, ar: "ب", en: "B", batch: "L", expiry: Future, quantity: 1, cost: 1m),
            Row(4, ar: "ب", en: "B", batch: "L", expiry: Future, quantity: 1, cost: 1m))).Value;

        preview.Rows[0].Status.ShouldBe(ImportRowStatus.Warning);
        Codes(preview.Rows[0].Warnings).ShouldContain(ImportWarnings.ExpiredBatch);
        Codes(preview.Rows[2].Warnings).ShouldContain(ImportWarnings.DuplicateBatchInFile);
        preview.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public async Task WithErrorsAndNoPermissionToSkip_NothingIsImported_ButSkippingImportsTheValidRows()
    {
        var good = Row(2, batch: "L", expiry: Future, quantity: 3, cost: 2m);
        var bad = Row(3, ar: "ب", en: "B", category: "Nope");

        var strict = await ImportAsync(false, null, good, bad);
        var productsAfterStrict = await CountAsync<Product>();
        var lenient = await ImportAsync(true, null, good, bad);

        strict.Error!.Code.ShouldBe(ImportErrors.HasErrors);
        productsAfterStrict.ShouldBe(1);
        (lenient.Value.ProductsCreated, lenient.Value.BatchesRecorded).ShouldBe((1, 1));
        lenient.Value.Rejected.Select(r => r.RowNumber).ShouldBe([3]);
        Codes(lenient.Value.Rejected.Single().Errors).ShouldContain(ImportErrors.UnknownCategory);
    }

    [Fact]
    public async Task WithoutTheOpeningStockPermission_OnlyTheCatalogIsImported_WithAWarning()
    {
        SignIn(WithoutStockPermission);

        var preview = (await PreviewAsync(Row(2, batch: "L", expiry: Future, quantity: 3, cost: 2m))).Value;
        var result = await ImportAsync(false, null, Row(2, batch: "L", expiry: Future, quantity: 3, cost: 2m));

        Codes(preview.Rows.Single().Warnings).ShouldContain(ImportWarnings.StockIgnored);
        (result.Value.ProductsCreated, result.Value.BatchesRecorded).ShouldBe((1, 0));
    }

    [Fact]
    public async Task ItNeedsTheCatalogManagePermission_AndAtLeastOneRow()
    {
        SignIn(Pharmacy.Application.Authorization.Permissions.CatalogView);
        var denied = await PreviewAsync(Row(2));
        SignIn(ReportReader);
        var empty = await PreviewAsync();

        denied.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        empty.Error!.Code.ShouldBe(ImportErrors.NoRows);
    }

    [Fact]
    public async Task APreview_ChangesNothing()
    {
        await PreviewAsync(Row(2, batch: "L", expiry: Future, quantity: 3, cost: 2m));

        (await CountAsync<Product>()).ShouldBe(1);
        (await CountAsync<Batch>()).ShouldBe(0);
    }

    [Fact]
    public async Task ABigImport_KeepsTheBooksBalanced()
    {
        var rows = Enumerable.Range(0, 120)
            .Select(i => Row(i + 2, ar: $"دواء {i}", en: $"Drug {i}", strength: $"{i} mg", batch: $"L{i}", expiry: Future, quantity: 5 + i, cost: 1m + i / 10m))
            .ToArray();

        var result = await ImportAsync(false, null, rows);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (result.Value.ProductsCreated, result.Value.BatchesRecorded).ShouldBe((120, 120));
        await AssertLedgerMatchesBatchesAsync();
    }
}
