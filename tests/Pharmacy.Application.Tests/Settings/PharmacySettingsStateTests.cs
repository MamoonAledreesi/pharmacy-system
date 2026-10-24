using Pharmacy.Application.Settings;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Application.Tests.Settings;

public class PharmacySettingsStateTests
{
    [Fact]
    public void Current_IsNullUntilSet()
    {
        new PharmacySettingsState().Current.ShouldBeNull();
    }

    [Fact]
    public void Set_StoresValueAndNotifiesEverySubscriber()
    {
        var state = new PharmacySettingsState();
        var updated = PharmacySettings.Defaults with { PrimaryColor = ThemeColor.Teal };
        var notifications = new List<PharmacySettingsData>();
        state.Changed += notifications.Add;
        state.Changed += notifications.Add;

        state.Set(updated);

        state.Current.ShouldBe(updated);
        notifications.ShouldBe([updated, updated]);
    }

    [Fact]
    public void Set_AfterUnsubscribe_DoesNotNotify()
    {
        var state = new PharmacySettingsState();
        var notified = false;
        void Handler(PharmacySettingsData _) => notified = true;
        state.Changed += Handler;
        state.Changed -= Handler;

        state.Set(PharmacySettings.Defaults);

        notified.ShouldBeFalse();
    }
}
