using MsixCore.Packaging.Bundles;

namespace MsixCore.Packaging.Tests;

public class Bcp47TagTests
{
    private static Bcp47Tag Tag(string text) => Bcp47Tag.Parse(text)!.Value;

    [Theory]
    [InlineData("fr", "fr", "", "")]
    [InlineData("fr-FR", "fr", "", "fr")]
    [InlineData("FR-fr", "fr", "", "fr")]
    [InlineData("zh-Hans-CN", "zh", "hans", "cn")]
    [InlineData("sr-Latn", "sr", "latn", "")]
    [InlineData("es-419", "es", "", "419")]
    [InlineData("und", "und", "", "")]
    public void Parse_ReadsLanguageScriptAndRegion(string input, string language, string script, string region)
    {
        Bcp47Tag tag = Tag(input);

        Assert.Equal(language, tag.Language);
        Assert.Equal(script, tag.Script);
        Assert.Equal(region, tag.Region);
    }

    [Fact]
    public void Parse_IgnoresVariantAndExtensionSubtags()
    {
        // de-DE-1901 must still match de-DE rather than being rejected outright.
        Bcp47Tag tag = Tag("de-DE-1901");

        Assert.Equal("de", tag.Language);
        Assert.Equal("de", tag.Region);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_ReturnsNull(string? input) => Assert.Null(Bcp47Tag.Parse(input));

    [Theory]
    // Chinese is conventionally written without a script, so region implies it.
    [InlineData("zh-CN", "zh", "hans", "cn")]
    [InlineData("zh-TW", "zh", "hant", "tw")]
    [InlineData("zh-HK", "zh", "hant", "hk")]
    public void Parse_NormalizesChineseScripts(string input, string language, string script, string region)
    {
        Bcp47Tag tag = Tag(input);

        Assert.Equal(language, tag.Language);
        Assert.Equal(script, tag.Script);
        Assert.Equal(region, tag.Region);
    }

    [Theory]
    [InlineData("fr-FR", "fr-FR", LanguageMatch.Exact)]
    [InlineData("fr-FR", "fr", LanguageMatch.Neutral)]
    [InlineData("fr", "fr-FR", LanguageMatch.Variant)]
    [InlineData("fr-FR", "fr-CA", LanguageMatch.Variant)]
    [InlineData("fr-FR", "de-DE", LanguageMatch.None)]
    [InlineData("zh-Hans", "zh-Hant", LanguageMatch.None)]
    [InlineData("zh-Hans-CN", "zh-Hans", LanguageMatch.Neutral)]
    [InlineData("zh-CN", "zh-Hans-CN", LanguageMatch.Exact)]
    [InlineData("und", "fr-FR", LanguageMatch.Undetermined)]
    [InlineData("fr-FR", "und", LanguageMatch.Undetermined)]
    public void Compare_ClassifiesMatches(string requested, string offered, LanguageMatch expected) =>
        Assert.Equal(expected, Bcp47Tag.Compare(Tag(requested), Tag(offered)));

    [Fact]
    public void Compare_IsCaseInsensitive() =>
        Assert.Equal(LanguageMatch.Exact, Bcp47Tag.Compare(Tag("FR-fr"), Tag("fr-FR")));

    [Fact]
    public void MatchStrength_IsOrdered()
    {
        // The engine relies on '>' to keep the best match, so the ordering is a contract.
        Assert.True(LanguageMatch.Exact > LanguageMatch.Neutral);
        Assert.True(LanguageMatch.Neutral > LanguageMatch.Undetermined);
        Assert.True(LanguageMatch.Undetermined > LanguageMatch.Variant);
        Assert.True(LanguageMatch.Variant > LanguageMatch.None);
    }

    [Fact]
    public void ToString_ReturnsNormalizedTag()
    {
        Assert.Equal("zh-hans-cn", Tag("zh-Hans-CN").ToString());
        Assert.Equal("fr", Tag("fr").ToString());
    }
}
