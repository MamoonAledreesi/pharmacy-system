using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Catalog;

public sealed class CatalogServiceTests : IAsyncLifetime
{
    // Ids seeded by the AddCatalog migration.
    private const int GeneralCategory = 1;
    private const int Tablets = 1;
    private const int Strip = 3;
    private const int Box = 4;
    private const int Pack = 5;

    private static readonly ProductDetails _amoxicillin = new(
        "أموكسيسيلين", "Amoxil", "Amoxicillin", "500 mg", "GSK", Tablets, GeneralCategory, null, true, 5, null);

    private readonly TestServices _services = new();

    public async Task InitializeAsync()
    {
        await _services.InitializeAsync();
        _services.CurrentUser.SignInAs(1, "pharmacist", isAdministrator: false, Permissions.CatalogView, Permissions.CatalogManage);
    }

    public Task DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task Migration_SeedsListsAndDefaultRolePermissions()
    {
        var units = await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().GetAllAsync(includeInactive: true));
        var forms = await _services.InScopeAsync(s => s.Lookup<DosageForm>().GetAllAsync(includeInactive: true));

        units.Value.Select(u => u.NameEnglish).ShouldContain("Strip");
        units.Value.Select(u => u.NameArabic).ShouldContain("باكت");
        forms.Value.Count.ShouldBe(11);

        var grants = await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().RoleClaims
            .Where(c => c.ClaimValue!.StartsWith("Catalog."))
            .Join(s.GetRequiredService<PharmacyDbContext>().Roles, c => c.RoleId, r => r.Id, (c, r) => r.Name + ":" + c.ClaimValue)
            .ToListAsync());
        grants.ShouldBe(
            ["Pharmacist:Catalog.View", "Pharmacist:Catalog.Manage", "Cashier:Catalog.View", "Accountant:Catalog.View",
             "StoreKeeper:Catalog.View", "StoreKeeper:Catalog.Manage"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task CreateProduct_WithUnitsAndWithoutBarcode_ThenLoadForEdit()
    {
        var id = (await CreateAsync(_amoxicillin, StripAndBox(barcode: null))).Value;

        var loaded = (await Products(s => s.GetForEditAsync(id))).Value;

        loaded.Details.ShouldBe(_amoxicillin);
        loaded.IsActive.ShouldBeTrue();
        loaded.Units.Select(u => (u.UnitOfMeasureId, u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode))
            .ShouldBe([(Strip, true, 1, 12m, (string?)null), (Box, false, 3, 33m, (string?)null)]);
        (await AuditActionsAsync()).ShouldContain(AuditActions.ProductCreated);
    }

    [Fact]
    public async Task Barcode_IsOptionalButUniqueAcrossProducts()
    {
        (await CreateAsync(_amoxicillin, StripAndBox(barcode: "6281000000017"))).IsSuccess.ShouldBeTrue();
        (await CreateAsync(_amoxicillin with { TradeNameEnglish = "No barcode" }, StripAndBox(barcode: null))).IsSuccess.ShouldBeTrue();

        var duplicate = await CreateAsync(_amoxicillin with { TradeNameEnglish = "Copy" }, StripAndBox(barcode: "6281000000017"));

        duplicate.Error!.Code.ShouldBe(CatalogErrors.DuplicateBarcode);
    }

    [Fact]
    public async Task UnknownReferences_AreRejected()
    {
        (await CreateAsync(_amoxicillin with { CategoryId = 999 }, StripAndBox(null))).Error!.Code.ShouldBe(CatalogErrors.UnknownCategory);
        (await CreateAsync(_amoxicillin with { DosageFormId = 999 }, StripAndBox(null))).Error!.Code.ShouldBe(CatalogErrors.UnknownDosageForm);
        (await CreateAsync(_amoxicillin, [new(null, 999, true, 1, null, null)])).Error!.Code.ShouldBe(CatalogErrors.UnknownUnit);
    }

    [Fact]
    public async Task UpdateProduct_CanSwapTheBaseUnitAndAddUnits()
    {
        var id = (await CreateAsync(_amoxicillin, StripAndBox(null))).Value;
        var loaded = (await Products(s => s.GetForEditAsync(id))).Value;
        var strip = loaded.Units.Single(u => u.UnitOfMeasureId == Strip);
        var box = loaded.Units.Single(u => u.UnitOfMeasureId == Box);

        // The pharmacy now also sells packs of 10 boxes, and decides the box is the smallest unit it sells.
        ProductUnitData[] units =
        [
            box with { IsBase = true, Factor = 1 },
            strip with { UnitOfMeasureId = Pack, IsBase = false, Factor = 10 },
        ];
        var result = await Products(s => s.UpdateAsync(id, loaded.Details with { Strength = "250 mg" }, units, loaded.RowVersion));

        result.IsSuccess.ShouldBeTrue();
        var reloaded = (await Products(s => s.GetForEditAsync(id))).Value;
        reloaded.Details.Strength.ShouldBe("250 mg");
        reloaded.Units.Select(u => (u.UnitOfMeasureId, u.IsBase, u.Factor)).ShouldBe([(Box, true, 1), (Pack, false, 10)]);
    }

    [Fact]
    public async Task UpdateProduct_WithStaleVersion_IsRejected_EvenWhenOnlyUnitsChange()
    {
        var id = (await CreateAsync(_amoxicillin, StripAndBox(null))).Value;
        var first = (await Products(s => s.GetForEditAsync(id))).Value;
        var second = (await Products(s => s.GetForEditAsync(id))).Value;

        (await Products(s => s.UpdateAsync(id, first.Details, first.Units, first.RowVersion))).IsSuccess.ShouldBeTrue();
        var stale = await Products(s => s.UpdateAsync(id, second.Details, [.. second.Units.Take(1)], second.RowVersion));

        stale.Error!.Code.ShouldBe(CommonErrors.ConcurrencyConflict);
        (await Products(s => s.GetForEditAsync(id))).Value.Units.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_IgnoresArabicSpellingVariants_AndMatchesEveryWord()
    {
        await CreateAsync(_amoxicillin, StripAndBox("6281000000017"));
        await CreateAsync(_amoxicillin with { TradeNameArabic = "بنادول إكسترا", TradeNameEnglish = "Panadol Extra", ScientificName = "Paracetamol" }, StripAndBox(null));

        (await QuickSearchNamesAsync("اموكسيسيلين")).ShouldBe(["Amoxil"]);
        (await QuickSearchNamesAsync("أموكسيسيلين")).ShouldBe(["Amoxil"]);
        (await QuickSearchNamesAsync("AMOX")).ShouldBe(["Amoxil"]);
        (await QuickSearchNamesAsync("paracetamol")).ShouldBe(["Panadol Extra"]);
        (await QuickSearchNamesAsync("extra panadol")).ShouldBe(["Panadol Extra"]);
        (await QuickSearchNamesAsync("6281000000017")).ShouldBe(["Amoxil"]);
        (await QuickSearchNamesAsync("بنادول اكسترا")).ShouldBe(["Panadol Extra"]);
        (await QuickSearchNamesAsync("not a product")).ShouldBeEmpty();
    }

    [Fact]
    public async Task QuickSearch_HidesInactiveProducts_ButTheListCanShowThem()
    {
        var id = (await CreateAsync(_amoxicillin, StripAndBox(null))).Value;
        (await Products(s => s.SetActiveAsync(id, false))).IsSuccess.ShouldBeTrue();

        (await QuickSearchNamesAsync("amoxil")).ShouldBeEmpty();
        var withInactive = await Query(s => s.SearchAsync(new ProductQuery("amoxil", null, IncludeInactive: true, 0, 25)));
        withInactive.Value.Items.Single().IsActive.ShouldBeFalse();
        (await AuditActionsAsync()).ShouldContain(AuditActions.ProductStatusChanged);
    }

    [Fact]
    public async Task List_FiltersByCategory_AndPages()
    {
        var otherCategory = (await _services.InScopeAsync(s => s.Lookup<Category>().CreateAsync("مسكنات", "Painkillers"))).Value;
        for (var i = 1; i <= 7; i++)
        {
            await CreateAsync(_amoxicillin with { TradeNameEnglish = $"Product {i}", CategoryId = i <= 3 ? otherCategory : GeneralCategory }, StripAndBox(null));
        }

        var page = await Query(s => s.SearchAsync(new ProductQuery(null, otherCategory, false, PageIndex: 1, PageSize: 2)));

        page.Value.TotalCount.ShouldBe(3);
        page.Value.Items.Single().TradeNameEnglish.ShouldBe("Product 3");
        page.Value.Items.Single().Category.English.ShouldBe("Painkillers");
        page.Value.Items.Single().Units.Select(u => u.Unit.English).ShouldBe(["Strip", "Box"]);
    }

    [Fact]
    public async Task QuickSearch_IsFastOnAThousandProducts()
    {
        await _services.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<PharmacyDbContext>();
            for (var i = 0; i < 1000; i++)
            {
                db.Products.Add(Product.Create(_amoxicillin with { TradeNameEnglish = $"Medicine {i:D4}", TradeNameArabic = $"دواء {i}" },
                    StripAndBox(null), DateTime.UtcNow).Value);
            }

            await db.SaveChangesAsync();
        });
        await QuickSearchNamesAsync("warm-up");

        var stopwatch = Stopwatch.StartNew();
        var results = await QuickSearchNamesAsync("medicine 05");
        stopwatch.Stop();

        results.Count.ShouldBe(ProductQueryService.QuickSearchLimit);
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(300, "the approved target for product search is under 300 ms");
    }

    [Fact]
    public async Task Lookups_CreateUpdateAndRejectDuplicates()
    {
        var created = await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().CreateAsync("فيال", "Vial"));
        created.IsSuccess.ShouldBeTrue();

        (await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().CreateAsync("فيال", "Another"))).Error!.Code.ShouldBe(CatalogErrors.NameTaken);
        (await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().CreateAsync("آخر", "BOX"))).Error!.Code.ShouldBe(CatalogErrors.NameTaken);
        (await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().UpdateAsync(created.Value, "فيال", "Vial", isActive: false))).IsSuccess.ShouldBeTrue();

        (await _services.InScopeAsync(s => s.Lookup<UnitOfMeasure>().GetAllAsync(includeInactive: false))).Value.ShouldNotContain(u => u.NameEnglish == "Vial");
        (await AuditActionsAsync()).ShouldContain(AuditActions.LookupUpdated);
    }

    [Fact]
    public async Task ViewOnlyUser_CanReadButNotChange()
    {
        var id = (await CreateAsync(_amoxicillin, StripAndBox(null))).Value;
        _services.CurrentUser.SignInAs(2, "cashier", isAdministrator: false, Permissions.CatalogView);

        (await Products(s => s.GetForEditAsync(id))).IsSuccess.ShouldBeTrue();
        (await QuickSearchNamesAsync("amoxil")).ShouldBe(["Amoxil"]);
        (await CreateAsync(_amoxicillin, StripAndBox(null))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await Products(s => s.SetActiveAsync(id, false))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await _services.InScopeAsync(s => s.Lookup<Category>().CreateAsync("س", "X"))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    [Fact]
    public async Task UserWithoutCatalogPermissions_CannotRead()
    {
        _services.CurrentUser.SignInAs(3, "nobody");

        (await Query(s => s.SearchAsync(new ProductQuery(null, null, false, 0, 25)))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await _services.InScopeAsync(s => s.Lookup<Category>().GetAllAsync(false))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    private static ProductUnitData[] StripAndBox(string? barcode) =>
    [
        new(null, Strip, true, 1, 12m, null),
        new(null, Box, false, 3, 33m, barcode),
    ];

    private Task<Result<int>> CreateAsync(ProductDetails details, IReadOnlyList<ProductUnitData> units) =>
        Products(s => s.CreateAsync(details, units));

    private Task<T> Products<T>(Func<ProductService, Task<T>> call) =>
        _services.InScopeAsync(s => call(s.GetRequiredService<ProductService>()));

    private Task<T> Query<T>(Func<ProductQueryService, Task<T>> call) =>
        _services.InScopeAsync(s => call(s.GetRequiredService<ProductQueryService>()));

    private async Task<List<string>> QuickSearchNamesAsync(string text) =>
        [.. (await Query(s => s.QuickSearchAsync(text))).Value.Select(p => p.TradeNameEnglish!)];

    private Task<List<string>> AuditActionsAsync() =>
        _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.Select(e => e.Action).ToListAsync());
}

internal static class CatalogServiceProviderExtensions
{
    public static LookupService<TLookup> Lookup<TLookup>(this IServiceProvider services)
        where TLookup : NamedLookup, new() => services.GetRequiredService<LookupService<TLookup>>();
}
