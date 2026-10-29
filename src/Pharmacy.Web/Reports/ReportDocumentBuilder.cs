using System.Globalization;
using Microsoft.Extensions.Localization;
using Pharmacy.Application.Reports;
using Pharmacy.Application.Settings;
using Pharmacy.Domain.Settings;
using Pharmacy.Web.Localization;

namespace Pharmacy.Web.Reports;

/// <summary>
/// Turns a finished <see cref="ReportTable"/> into a <see cref="ReportDocument"/> in the current UI language:
/// every key becomes its translation and every bilingual name is picked. The screen and both exports use the result.
/// </summary>
public sealed class ReportDocumentBuilder(IStringLocalizer<SharedResource> localizer, PharmacySettingsState settingsState, TimeProvider clock)
{
    public ReportDocument Build(ReportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var culture = CultureInfo.CurrentUICulture;
        var arabic = SupportedCultures.IsArabicUi;
        var pharmacy = (settingsState.Current ?? PharmacySettings.Defaults).DisplayName(culture);

        return new ReportDocument(
            localizer[$"Reports.Title.{table.Key}"],
            pharmacy,
            $"{localizer["Reports.GeneratedAt"]}: ⁦{clock.GetLocalNow().DateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}⁩",
            arabic,
            [.. table.Filters.Select(f => new ReportDocumentFilter(localizer[f.LabelKey], FilterText(f.Value, culture, arabic)))],
            [.. table.Columns.Select(c => new ReportDocumentColumn(localizer[c.Key], c.Kind))],
            [.. table.Rows.Select(row => Translate(row, arabic))],
            table.Totals is null ? null : Translate(table.Totals, arabic),
            localizer["Reports.Page"]);
    }

    private IReadOnlyList<object?> Translate(IReadOnlyList<object?> row, bool arabic) =>
        [.. row.Select(cell => cell switch
        {
            ReportText text => localizer[text.Key].Value,
            LocalizedText name => arabic ? name.Arabic : name.English,
            _ => cell,
        })];

    private string FilterText(object value, CultureInfo culture, bool arabic) => value switch
    {
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        LocalizedText name => arabic ? name.Arabic : name.English,
        ReportText text => localizer[text.Key].Value,
        _ => Convert.ToString(value, culture) ?? string.Empty,
    };
}
