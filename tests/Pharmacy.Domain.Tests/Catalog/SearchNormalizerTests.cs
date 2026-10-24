using Pharmacy.Domain.Catalog;

namespace Pharmacy.Domain.Tests.Catalog;

public class SearchNormalizerTests
{
    [Theory]
    [InlineData("أموكسيسيلين", "اموكسيسيلين")]
    [InlineData("إيبوبروفين", "ايبوبروفين")]
    [InlineData("آمن", "امن")]
    [InlineData("حبة", "حبه")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("مُسَكِّن", "مسكن")]
    [InlineData("بنـــادول", "بنادول")]
    [InlineData("مؤقت", "موقت")]
    [InlineData("شائع", "شايع")]
    [InlineData("فيتامين ٥٠٠", "فيتامين 500")]
    [InlineData("PANADOL Extra", "panadol extra")]
    [InlineData("  panadol   extra  ", "panadol extra")]
    public void Normalize_IgnoresSpellingVariantsCaseAndSpacing(string input, string expected)
    {
        SearchNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyInput_ReturnsEmpty(string? input)
    {
        SearchNormalizer.Normalize(input).ShouldBe(string.Empty);
    }

    [Fact]
    public void Normalize_DifferentSpellingsOfTheSameWord_Match()
    {
        SearchNormalizer.Normalize("أموكسيسيلين").ShouldBe(SearchNormalizer.Normalize("اموكسيسيلين"));
        SearchNormalizer.Normalize("كبسولة").ShouldBe(SearchNormalizer.Normalize("كبسوله"));
    }
}
