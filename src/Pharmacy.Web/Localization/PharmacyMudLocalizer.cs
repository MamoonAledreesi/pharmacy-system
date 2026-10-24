using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Pharmacy.Web.Localization;

/// <summary>
/// Translates MudBlazor's built-in texts (pager, close buttons, date picker, ...) from our resources,
/// keyed "MudBlazor.{MudBlazor key}". Keys we do not define fall back to MudBlazor's English text.
/// </summary>
internal sealed class PharmacyMudLocalizer(IStringLocalizer<SharedResource> localizer) : MudLocalizer
{
    public override LocalizedString this[string key] => localizer[$"MudBlazor.{key}"];

    public override LocalizedString this[string key, params object[] arguments] => localizer[$"MudBlazor.{key}", arguments];
}
