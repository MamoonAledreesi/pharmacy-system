using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Settings;
using Pharmacy.Web.Common;

namespace Pharmacy.ArchitectureTests;

/// <summary>
/// Guards the bilingual requirement: every UI string exists, non-empty, in both Arabic and English.
/// </summary>
public partial class LocalizationResourceTests
{
    private static readonly string _webProjectDirectory = Path.Combine(FindRepositoryRoot(), "src", "Pharmacy.Web");
    private static readonly string _resourcesDirectory = Path.Combine(_webProjectDirectory, "Resources");

    [Fact]
    public void ArabicAndEnglish_HaveTheSameKeys()
    {
        var arabic = LoadResource("SharedResource.ar.resx");
        var english = LoadResource("SharedResource.en.resx");

        arabic.Keys.Except(english.Keys).ShouldBeEmpty("keys missing from the English resource");
        english.Keys.Except(arabic.Keys).ShouldBeEmpty("keys missing from the Arabic resource");
    }

    [Theory]
    [InlineData("SharedResource.ar.resx")]
    [InlineData("SharedResource.en.resx")]
    public void EveryValue_IsTranslated(string fileName)
    {
        var empty = LoadResource(fileName).Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key);

        empty.ShouldBeEmpty($"{fileName} has empty values");
    }

    [Fact]
    public void EveryKeyUsedInRazorComponents_Exists()
    {
        var keys = LoadResource("SharedResource.en.resx").Keys.ToHashSet(StringComparer.Ordinal);

        var missing = Directory.EnumerateFiles(_webProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => LocalizerKeyPattern().Matches(File.ReadAllText(file))
                .Select(match => (File: Path.GetFileName(file), Key: match.Groups["key"].Value)))
            .Where(usage => !keys.Contains(usage.Key))
            .Select(usage => $"{usage.File}: {usage.Key}")
            .Distinct();

        missing.ShouldBeEmpty("razor components use keys that are not defined");
    }

    [Fact]
    public void EveryErrorCode_HasAMessage()
    {
        // Error codes are public string constants on *Errors classes (e.g. SettingsErrors, CommonErrors).
        var keys = LoadResource("SharedResource.en.resx").Keys.ToHashSet(StringComparer.Ordinal);
        Assembly[] assemblies = [typeof(Result).Assembly, typeof(CommonErrors).Assembly];

        var codes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        codes.ShouldNotBeEmpty();
        codes.Where(code => !keys.Contains(code)).ShouldBeEmpty("error codes without a localized message");
    }

    [Fact]
    public void EverySettingsOption_HasALabel()
    {
        var keys = LoadResource("SharedResource.en.resx").Keys.ToHashSet(StringComparer.Ordinal);

        var expected = Enum.GetNames<ThemeMode>().Select(name => $"Settings.ThemeMode.{name}")
            .Concat(Enum.GetNames<ThemeColor>().Select(name => $"Settings.Color.{name}"));

        expected.Where(key => !keys.Contains(key)).ShouldBeEmpty("settings options without a label");
    }

    [Fact]
    public void EveryPermissionModuleAuditActionAndBuiltInRole_HasALabel()
    {
        var keys = LoadResource("SharedResource.en.resx").Keys.ToHashSet(StringComparer.Ordinal);

        var expected = Permissions.All.Select(p => $"Permissions.{p}")
            .Concat(Permissions.All.Select(Permissions.ModuleOf).Distinct().Select(m => $"PermissionModules.{m}"))
            .Concat(AuditActions.All.Select(a => $"AuditActions.{a}"))
            .Concat(SystemRoles.Defaults.Select(r => $"RoleNames.{r}"));

        expected.Where(key => !keys.Contains(key)).ShouldBeEmpty("labels missing from the resources");
    }

    [Fact]
    public void EveryInventoryOption_HasALabel()
    {
        var keys = LoadResource("SharedResource.en.resx").Keys.ToHashSet(StringComparer.Ordinal);

        var expected = Enum.GetNames<StockFilter>().Select(name => $"StockFilters.{name}")
            .Concat(Enum.GetNames<StockAction>().Select(name => $"StockActions.{name}"))
            .Concat(Enum.GetNames<StockMovementType>().Select(name => $"StockMovementTypes.{name}"))
            .Concat(StockReasons.All.Select(reason => $"StockReasons.{reason}"));

        expected.Where(key => !keys.Contains(key)).ShouldBeEmpty("inventory options without a label");
    }

    // Matches L["Some.Key"] and L["Some.Key", arg] in markup.
    [GeneratedRegex("""\bL\["(?<key>[^"]+)"[\],]""")]
    private static partial Regex LocalizerKeyPattern();

    private static Dictionary<string, string> LoadResource(string fileName) =>
        XDocument.Load(Path.Combine(_resourcesDirectory, fileName))
            .Root!
            .Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PharmacySystem.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate PharmacySystem.slnx above the test output directory.");
    }
}
