using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Tests against real SignTool-produced packages. These validate that every assumption about the
/// APPX signature format holds against genuine Microsoft tooling output — not just synthetic fixtures.
/// </summary>
public sealed class RealSignedPackageTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealSigned", name);

    #region SignTest.msix — legitimate real signature

    [Fact]
    public void SignTest_SignatureParsesCorrectly()
    {
        using MsixPackage package = MsixPackage.Open(FixturePath("SignTest.msix"));
        PackageSignature? sig = package.ReadSignature();

        Assert.NotNull(sig);
        Assert.True(sig.IsCmsIntegrityValid);
        Assert.NotNull(sig.DigestTable);
        Assert.Null(sig.DigestTableError);
    }

    [Fact]
    public void SignTest_DigestTable_HasExpectedStructure()
    {
        using MsixPackage package = MsixPackage.Open(FixturePath("SignTest.msix"));
        PackageSignature? sig = package.ReadSignature();
        Assert.NotNull(sig);

        AppxDigestTable table = sig.DigestTable!;

        // 4 entries: AXPC, AXCD, AXCT, AXBM — in that order. No AXCI.
        Assert.Equal(4, table.Entries.Count);
        Assert.Equal(AppxDigestTag.Axpc, table.Entries[0].Tag);
        Assert.Equal(AppxDigestTag.Axcd, table.Entries[1].Tag);
        Assert.Equal(AppxDigestTag.Axct, table.Entries[2].Tag);
        Assert.Equal(AppxDigestTag.Axbm, table.Entries[3].Tag);

        // Each digest is 32 bytes (SHA-256).
        foreach (AppxDigestEntry entry in table.Entries)
        {
            Assert.Equal(32, entry.Digest.Length);
        }
    }

    [Fact]
    public void SignTest_BindingVerification_IsValid()
    {
        using MsixPackage package = MsixPackage.Open(FixturePath("SignTest.msix"));
        PackageSignature? sig = package.ReadSignature();
        Assert.NotNull(sig);

        IndirectDataBindingResult binding = package.VerifySignatureBinding(sig);

        Assert.True(binding.IsBindingValid);

        // AXCT and AXBM should be Valid; AXPC and AXCD should be NotVerified.
        var resultsByTag = binding.Results.ToDictionary(r => r.Tag);
        Assert.Equal(DigestVerificationStatus.NotVerified, resultsByTag[AppxDigestTag.Axpc].Status);
        Assert.Equal(DigestVerificationStatus.NotVerified, resultsByTag[AppxDigestTag.Axcd].Status);
        Assert.Equal(DigestVerificationStatus.Valid, resultsByTag[AppxDigestTag.Axct].Status);
        Assert.Equal(DigestVerificationStatus.Valid, resultsByTag[AppxDigestTag.Axbm].Status);
    }

    [Fact]
    public void SignTest_BlockMapVerification_Passes()
    {
        using MsixPackage package = MsixPackage.Open(FixturePath("SignTest.msix"));
        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.True(result.IsValid);
    }

    #endregion

    #region Stapled.msix — real signature stapled onto tampered package

    [Fact]
    public void Stapled_CmsEnvelopeIsValid_ButBindingFails()
    {
        // This is the headline test: the CMS envelope is a genuine Microsoft-tooling-produced
        // signature, but it was stapled onto a different package. The CMS itself is still
        // internally consistent (it is a real signature), but binding must fail because
        // AXCT and AXBM digests no longer match the actual package content.
        using MsixPackage package = MsixPackage.Open(FixturePath("Stapled.msix"));
        PackageSignature? sig = package.ReadSignature();
        Assert.NotNull(sig);

        // CMS envelope is valid — it is a genuine, unmodified signature.
        Assert.True(sig.IsCmsIntegrityValid);
        Assert.NotNull(sig.DigestTable);

        // But binding FAILS — the digest table was signed over different content.
        IndirectDataBindingResult binding = package.VerifySignatureBinding(sig);
        Assert.False(binding.IsBindingValid);

        // Specifically, AXCT and/or AXBM must be Mismatch.
        var mismatches = binding.Results
            .Where(r => r.Status == DigestVerificationStatus.Mismatch)
            .Select(r => r.Tag)
            .ToHashSet();

        Assert.Contains(AppxDigestTag.Axbm, mismatches);
    }

    #endregion

    #region Tag canonical names in output

    [Fact]
    public void TagSpecNames_AreUppercase()
    {
        Assert.Equal("AXPC", AppxDigestTag.Axpc.ToSpecName());
        Assert.Equal("AXCD", AppxDigestTag.Axcd.ToSpecName());
        Assert.Equal("AXCT", AppxDigestTag.Axct.ToSpecName());
        Assert.Equal("AXBM", AppxDigestTag.Axbm.ToSpecName());
        Assert.Equal("AXCI", AppxDigestTag.Axci.ToSpecName());
    }

    [Fact]
    public void SignTest_BindingResults_UseCanonicalUppercaseTagNames()
    {
        // Verify that when tags flow through to results, ToSpecName() produces uppercase.
        using MsixPackage package = MsixPackage.Open(FixturePath("SignTest.msix"));
        PackageSignature? sig = package.ReadSignature();
        Assert.NotNull(sig);

        IndirectDataBindingResult binding = package.VerifySignatureBinding(sig);

        foreach (DigestEntryResult r in binding.Results)
        {
            string specName = r.Tag.ToSpecName();
            Assert.Equal(specName.ToUpperInvariant(), specName);
        }
    }

    #endregion
}
