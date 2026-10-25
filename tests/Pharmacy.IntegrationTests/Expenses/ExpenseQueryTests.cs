using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Expenses;
using Pharmacy.Domain.Catalog;

namespace Pharmacy.IntegrationTests.Expenses;

public sealed class ExpenseQueryTests : ExpensesTestBase
{
    [Fact]
    public async Task Search_FiltersByCategoryDateAndText_NewestDateFirst()
    {
        await OpenAsync(10_000m);
        var rent = await RecordAsync(500m, RentCategory, Today.AddDays(-10), "June rent");
        var electricity = await RecordAsync(80m, ElectricityCategory, Today.AddDays(-2), "electricity bill");
        var latest = await RecordAsync(20m, RentCategory, Today, "late fee");

        var all = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, null, null, null, false, 0, 25)))).Value;
        var byCategory = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, RentCategory, null, null, false, 0, 25)))).Value;
        var byDates = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, null, Today.AddDays(-3), Today, false, 0, 25)))).Value;
        var byText = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery("bill", null, null, null, false, 0, 25)))).Value;

        all.Items.Select(i => i.Id).ShouldBe([latest, electricity, rent]);
        byCategory.Items.Select(i => i.Id).ShouldBe([latest, rent]);
        byDates.Items.Select(i => i.Id).ShouldBe([latest, electricity]);
        byText.Items.Select(i => i.Id).ShouldBe([electricity]);
        all.Items.Last().Category.English.ShouldBe("Rent");
    }

    [Fact]
    public async Task ReversedExpenses_AreHiddenByDefault_AndNeverCounted()
    {
        await OpenAsync(10_000m);
        var kept = await RecordAsync(100m, RentCategory);
        var wrong = await RecordAsync(900m, RentCategory);
        await Expenses(s => s.ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), wrong, "typo")));

        var hidden = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, null, null, null, false, 0, 25)))).Value;
        var shown = (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, null, null, null, true, 0, 25)))).Value;
        var totals = (await ExpenseQuery(s => s.GetTotalsAsync(new ExpenseQuery(null, null, null, null, true, 0, 1)))).Value;

        hidden.Items.Select(i => i.Id).ShouldBe([kept]);
        shown.Items.Select(i => i.Id).ShouldBe([wrong, kept]);
        shown.Items.First().IsReversed.ShouldBeTrue();
        totals.Total.ShouldBe(100m);
        totals.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Totals_AreGroupedByCategory_BiggestFirst()
    {
        await OpenAsync(10_000m);
        await RecordAsync(500m, RentCategory);
        await RecordAsync(80m, ElectricityCategory);
        await RecordAsync(70m, ElectricityCategory);
        await RecordAsync(40m, RentCategory, Today.AddDays(-60));

        var thisMonth = (await ExpenseQuery(s => s.GetTotalsAsync(new ExpenseQuery(null, null, Today.AddDays(-30), Today, false, 0, 1)))).Value;
        var everything = (await ExpenseQuery(s => s.GetTotalsAsync(new ExpenseQuery(null, null, null, null, false, 0, 1)))).Value;

        thisMonth.Total.ShouldBe(650m);
        thisMonth.ByCategory.Select(c => (c.Category.English, c.Total, c.Count)).ShouldBe([("Rent", 500m, 1), ("Electricity", 150m, 2)]);
        everything.Total.ShouldBe(690m);
        everything.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Queries_WithoutViewPermission_AreForbidden()
    {
        SignIn(7, "nobody");

        (await ExpenseQuery(s => s.SearchAsync(new ExpenseQuery(null, null, null, null, false, 0, 25)))).IsFailure.ShouldBeTrue();
        (await ExpenseQuery(s => s.GetTotalsAsync(new ExpenseQuery(null, null, null, null, false, 0, 1)))).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TheCategoryList_StartsWithTheDefaults_AndServesWhoeverMayRecordExpenses()
    {
        var all = (await Categories(s => s.GetAllAsync(includeInactive: false))).Value;
        SignIn(7, "recorder", Permissions.ExpensesCreate);
        var forRecorder = await Categories(s => s.GetAllAsync(includeInactive: false));
        SignIn(7, "nobody");
        var forNobody = await Categories(s => s.GetAllAsync(includeInactive: false));

        all.Count.ShouldBe(11);
        all.Select(c => c.NameEnglish).ShouldContain("Rent");
        forRecorder.IsSuccess.ShouldBeTrue();
        forNobody.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Categories_CanBeAddedRenamedAndDeactivated_ButNotDuplicated()
    {
        var created = await Categories(s => s.CreateAsync("تأمين", "Insurance"));
        var duplicate = await Categories(s => s.CreateAsync("إيجار", "Another"));
        var duplicateEnglish = await Categories(s => s.CreateAsync("آخر", "rent"));
        var updated = await Categories(s => s.UpdateAsync(created.Value, "تأمين صحي", "Health insurance", isActive: false));

        created.IsSuccess.ShouldBeTrue(created.Error?.Code);
        ShouldFailWith(duplicate, CatalogErrors.NameTaken);
        ShouldFailWith(duplicateEnglish, CatalogErrors.NameTaken);
        updated.IsSuccess.ShouldBeTrue(updated.Error?.Code);
        var active = (await Categories(s => s.GetAllAsync(includeInactive: false))).Value;
        var every = (await Categories(s => s.GetAllAsync(includeInactive: true))).Value;
        active.Count.ShouldBe(11);
        every.Count.ShouldBe(12);
        every.Last().NameEnglish.ShouldBe("Health insurance");
        (await AuditActionsAsync()).ShouldContain(AuditActions.ExpenseCategoryCreated);
        (await AuditActionsAsync()).ShouldContain(AuditActions.ExpenseCategoryUpdated);
    }

    [Fact]
    public async Task ManagingCategories_NeedsThePermission_AndANameInBothLanguages()
    {
        var noArabic = await Categories(s => s.CreateAsync(" ", "Insurance"));
        SignIn(7, "creator", Permissions.ExpensesView, Permissions.ExpensesCreate);
        var forbidden = await Categories(s => s.CreateAsync("تأمين", "Insurance"));

        ShouldFailWith(noArabic, CatalogErrors.NameArabicRequired);
        ShouldFailWith(forbidden, CommonErrors.Forbidden);
    }
}
