using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Corpus.Tests;

/// <summary>
/// Data-driven regression suite: for every fixture in <c>corpus.json</c> it opens the package with
/// our library (loose and/or packed) and asserts the parsed values equal the expected values that
/// were derived independently of the library (via System.Xml + an independent publisher-hash
/// implementation) and cross-checked against the real Windows deployment oracle at generation time.
/// </summary>
public sealed class CorpusTests
{
    [Fact]
    public void Corpus_Is_Loaded_And_NonEmpty()
    {
        Assert.True(File.Exists(Path.Combine(CorpusRepository.CorpusRoot, "corpus.json")));
        Assert.NotEmpty(CorpusRepository.Document.Fixtures);
        Assert.Equal(CorpusRepository.Document.Meta.FixtureCount, CorpusRepository.Document.Fixtures.Count);
    }

    [Theory]
    [MemberData(nameof(CorpusRepository.LooseCases), MemberType = typeof(CorpusRepository))]
    public void LooseLayout_ParsesToExpectedValues(string id)
    {
        CorpusFixture fx = CorpusRepository.Get(id);
        string dir = CorpusRepository.ResolvePath(fx.LooseDir!);
        Assert.True(Directory.Exists(dir), $"Loose fixture directory missing: {dir}");

        using MsixPackage package = MsixPackage.OpenDirectory(dir);

        AssertIdentityAndMetadata(package, fx);
        AssertSignature(package, fx.IsSignedLoose);
        Assert.Equal(fx.BlockMapValidLoose!.Value, package.VerifyBlockMap().IsValid);
    }

    [Theory]
    [MemberData(nameof(CorpusRepository.PackedPackageCases), MemberType = typeof(CorpusRepository))]
    public void PackedLayout_ParsesToExpectedValues(string id)
    {
        CorpusFixture fx = CorpusRepository.Get(id);
        string file = CorpusRepository.ResolvePath(fx.PackedFile!);
        Assert.True(File.Exists(file), $"Packed fixture missing: {file}");

        using MsixPackage package = MsixPackage.Open(file);

        AssertIdentityAndMetadata(package, fx);
        AssertSignature(package, fx.IsSignedPacked);

        if (fx.BlockMapFileCount is int expectedCount)
        {
            Assert.Equal(expectedCount, package.BlockMap.Files.Count);
        }

        // Regression guard for issue #7: the percent-encoded-name fixture's packed block map must
        // validate now that the reader percent-decodes OPC part names. blockMapValidPacked is true
        // for every fixture; a regression in part-name decoding would flip it and fail here.
        Assert.Equal(fx.BlockMapValidPacked!.Value, package.VerifyBlockMap().IsValid);
    }

    [Theory]
    [MemberData(nameof(CorpusRepository.BundleCases), MemberType = typeof(CorpusRepository))]
    public void Bundle_DocumentedCurrentBehavior(string id)
    {
        CorpusFixture fx = CorpusRepository.Get(id);
        Assert.False(fx.ExpectedSupported, "Bundles are documented as not-yet-supported by the reader.");

        string file = CorpusRepository.ResolvePath(fx.PackedFile!);
        Assert.True(File.Exists(file), $"Bundle fixture missing: {file}");

        using MsixPackage package = MsixPackage.Open(file);

        // The container opens as an OPC package and carries the bundle manifest, but not an app
        // manifest. Bundle applicability is not implemented, so reading it as an app manifest
        // throws. This asserts the current documented behavior rather than the eventual ideal.
        Assert.True(package.Opc.ContainsPart(OpcPartNames.AppxBundleManifest));
        Assert.False(package.Opc.ContainsPart(OpcPartNames.AppxManifest));
        Assert.Throws<InvalidDataException>(() => package.Manifest);
    }

    private static void AssertIdentityAndMetadata(MsixPackage package, CorpusFixture fx)
    {
        ExpectedValues expected = fx.Expected!;
        Assert.NotNull(expected);

        PackageIdentity identity = package.Identity;
        Assert.Equal(expected.Name, identity.Name);
        Assert.Equal(expected.Publisher, identity.Publisher);
        Assert.Equal(expected.Version, identity.Version.ToString());
        Assert.Equal(expected.Architecture, identity.Architecture.ToString());
        Assert.Equal(expected.ResourceId, identity.ResourceId);
        Assert.Equal(expected.PackageFamilyName, identity.PackageFamilyName);
        Assert.Equal(expected.PackageFullName, identity.PackageFullName);

        Assert.Equal(expected.DisplayName, package.DisplayName);
        Assert.Equal(expected.PublisherDisplayName, package.PublisherDisplayName);
        Assert.Equal(expected.Capabilities, package.Capabilities);
        Assert.Equal(expected.IsFramework, package.Manifest.IsFramework);
        Assert.Equal(expected.ApplicationCount, package.Manifest.Applications.Count);
    }

    /// <summary>
    /// Asserts the package's signing state. For signed fixtures this goes beyond the presence of the
    /// <c>AppxSignature.p7x</c> part: the signature must actually parse, its CMS envelope must be
    /// internally consistent, and the signer subject DN must match the manifest publisher — otherwise
    /// a malformed or mismatched signature would satisfy a presence-only check.
    /// </summary>
    private static void AssertSignature(MsixPackage package, bool expectedSigned)
    {
        Assert.Equal(expectedSigned, package.IsSigned);

        PackageSignature? signature = package.ReadSignature();

        if (!expectedSigned)
        {
            Assert.Null(signature);
            return;
        }

        Assert.NotNull(signature);
        Assert.True(signature!.IsCmsIntegrityValid, "Signed fixture has an invalid CMS signature envelope.");
        Assert.True(
            signature.MatchesPublisher(package.Identity.Publisher),
            $"Signer subject '{signature.SubjectName}' does not match manifest publisher '{package.Identity.Publisher}'.");
    }
}
