using Pharmacy.Domain.Inventory;

namespace Pharmacy.Domain.Tests.Inventory;

public class ExpiryRulesTests
{
    private static readonly DateOnly _today = new(2026, 10, 24);

    [Theory]
    [InlineData(2027, 1, 31)]
    [InlineData(2027, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2027, 4, 30)]
    [InlineData(2027, 12, 31)]
    public void EndOfMonth_IsTheLastDayOfThatMonth(int year, int month, int expectedDay)
    {
        ExpiryRules.EndOfMonth(year, month).ShouldBe(new DateOnly(year, month, expectedDay));
    }

    [Theory]
    [InlineData(2027, 0)]
    [InlineData(2027, 13)]
    [InlineData(1999, 6)]
    [InlineData(2101, 6)]
    public void EndOfMonth_RejectsOutOfRangeValues(int year, int month)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ExpiryRules.EndOfMonth(year, month));
    }

    [Fact]
    public void ABatchExpiresAfterItsExpiryDay_NotOnIt()
    {
        ExpiryRules.IsExpired(_today.AddDays(-1), _today).ShouldBeTrue();
        ExpiryRules.IsExpired(_today, _today).ShouldBeFalse();
        ExpiryRules.IsExpired(_today.AddDays(1), _today).ShouldBeFalse();
        ExpiryRules.IsExpired(null, _today).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    [InlineData(-1, false)]
    public void NearExpiry_MeansSellableButExpiringWithinTheThreshold(int daysFromToday, bool expected)
    {
        ExpiryRules.IsNearExpiry(_today.AddDays(daysFromToday), _today, 90).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, ExpiryStatus.NoExpiry)]
    [InlineData(-5, ExpiryStatus.Expired)]
    [InlineData(10, ExpiryStatus.NearExpiry)]
    [InlineData(400, ExpiryStatus.Ok)]
    public void StatusOf_CombinesTheRules(int? daysFromToday, ExpiryStatus expected)
    {
        var expiry = daysFromToday is { } days ? _today.AddDays(days) : (DateOnly?)null;

        ExpiryRules.StatusOf(expiry, _today, 90).ShouldBe(expected);
    }

    [Fact]
    public void AProductsOwnThreshold_ChangesWhatCountsAsNear()
    {
        var expiry = _today.AddDays(40);

        ExpiryRules.StatusOf(expiry, _today, 30).ShouldBe(ExpiryStatus.Ok);
        ExpiryRules.StatusOf(expiry, _today, 60).ShouldBe(ExpiryStatus.NearExpiry);
    }
}
