namespace MsixCore.Deployment.Tests;

public class WildcardTests
{
    [Theory]
    [InlineData("Contoso.MyApp_1.0.0.0_x64__abc", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("contoso.myapp_1.0.0.0_x64__abc", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("Contoso.*", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("*_x64__*", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("Contoso.MyApp_?.0.0.0_x64__abc", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("*", "anything")]
    public void IsMatch_ReturnsTrue(string pattern, string input)
    {
        Assert.True(Wildcard.IsMatch(pattern, input));
    }

    [Theory]
    [InlineData("Fabrikam.*", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("Contoso.MyApp_??.0.0.0_x64__abc", "Contoso.MyApp_1.0.0.0_x64__abc")]
    [InlineData("Contoso", "Contoso.MyApp")]
    public void IsMatch_ReturnsFalse(string pattern, string input)
    {
        Assert.False(Wildcard.IsMatch(pattern, input));
    }

    [Fact]
    public void IsMatch_TreatsRegexMetacharactersLiterally()
    {
        Assert.True(Wildcard.IsMatch("a.b+c", "a.b+c"));
        Assert.False(Wildcard.IsMatch("a.b+c", "aXbXc"));
    }
}
