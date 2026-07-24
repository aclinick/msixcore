using MsixCore.Packaging;

namespace MsixCore.Packaging.Tests;

public class PublisherHashTests
{
    [Theory]
    // The canonical Microsoft Store publisher -> the well-known "8wekyb3d8bbwe".
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "8wekyb3d8bbwe")]
    public void Compute_ProducesKnownHash(string publisher, string expected)
    {
        Assert.Equal(expected, PublisherHash.Compute(publisher));
    }

    [Fact]
    public void Compute_IsThirteenChars_UsingMsixAlphabet()
    {
        string hash = PublisherHash.Compute("CN=Contoso Ltd, O=Contoso, C=US");

        Assert.Equal(13, hash.Length);
        Assert.All(hash, c => Assert.Contains(c, "0123456789abcdefghjkmnpqrstvwxyz"));
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        const string publisher = "CN=Contoso";
        Assert.Equal(PublisherHash.Compute(publisher), PublisherHash.Compute(publisher));
    }

    [Fact]
    public void Compute_IsCaseSensitive_OnPublisher()
    {
        Assert.NotEqual(PublisherHash.Compute("CN=Contoso"), PublisherHash.Compute("CN=contoso"));
    }

    [Fact]
    public void Compute_NullPublisher_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PublisherHash.Compute(null!));
    }
}
