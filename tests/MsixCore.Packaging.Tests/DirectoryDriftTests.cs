using System.Text;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public sealed class DirectoryDriftTests : IDisposable
{
    private const string Manifest =
        """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.DriftTest" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Drift Test</DisplayName>
            <PublisherDisplayName>Contoso</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"msixcore-drift-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VerifyBlockMap_DirectoryMutationAfterOpen_IsInvalid(bool addFile)
    {
        string directory = CreatePackage();
        using MsixPackage package = MsixPackage.OpenDirectory(directory);
        Assert.True(package.VerifyBlockMap().IsValid);

        if (addFile)
        {
            File.WriteAllText(Path.Combine(directory, "unmapped.txt"), "unmapped");
        }
        else
        {
            File.Delete(Path.Combine(directory, "Assets", "payload.txt"));
        }

        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.False(result.IsValid);
        Assert.Contains(result.CoverageErrors, error => error.Contains("Package snapshot drift detected", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockMapVerifier_PublicEntryPoints_DetectPostOpenAddition(bool verifyContent)
    {
        string directory = CreatePackage();
        using MsixPackage package = MsixPackage.OpenDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "unmapped.txt"), "unmapped");
        using var decorated = new DelegatingOpcPackage(package.Opc);

        IReadOnlyList<string> coverageErrors;
        if (verifyContent)
        {
            BlockMapVerificationResult result = BlockMapVerifier.Verify(decorated, package.BlockMap);
            Assert.False(result.IsValid);
            coverageErrors = result.CoverageErrors;
        }
        else
        {
            coverageErrors = BlockMapVerifier.VerifyCoverage(decorated, package.BlockMap);
        }

        string driftError = Assert.Single(
            coverageErrors,
            error => error.Contains("Package snapshot drift detected", StringComparison.Ordinal));
        Assert.Contains("unmapped.txt", driftError);
    }

    [Fact]
    public void DetectDirectoryDrift_CaseVariantCollisionAddedAfterOpen_IsDetected()
    {
        string directory = CreatePackage();
        if (!IsCaseSensitive(directory))
        {
            DirectoryOpcPackage.DirectoryPartEnumeration synthetic =
                DirectoryOpcPackage.EnumerateValidatedParts(
                    directory,
                    [
                        Path.Combine(directory, "Assets", "payload.txt"),
                        Path.Combine(directory, "ASSETS", "PAYLOAD.TXT"),
                    ]);
            Assert.Contains("duplicate part name", synthetic.Error, StringComparison.OrdinalIgnoreCase);
            return;
        }

        using MsixPackage package = MsixPackage.OpenDirectory(directory);
        string collisionDirectory = Path.Combine(directory, "ASSETS");
        Directory.CreateDirectory(collisionDirectory);
        File.WriteAllText(Path.Combine(collisionDirectory, "PAYLOAD.TXT"), "attacker");

        string? drift = package.DetectDirectoryDrift();

        Assert.NotNull(drift);
        Assert.Contains("duplicate part name", drift, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectDirectoryDrift_InvalidPartNameAddedAfterOpen_IsDetected()
    {
        string directory = CreatePackage();
        using MsixPackage package = MsixPackage.OpenDirectory(directory);
        string invalidPath = Path.Combine(directory, "invalid?.txt");
        try
        {
            File.WriteAllText(invalidPath, "attacker");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DirectoryOpcPackage.DirectoryPartEnumeration synthetic =
                DirectoryOpcPackage.EnumerateValidatedParts(directory, [invalidPath]);
            Assert.Contains("invalid part name", synthetic.Error, StringComparison.OrdinalIgnoreCase);
            return;
        }

        string? drift = package.DetectDirectoryDrift();

        Assert.NotNull(drift);
        Assert.Contains("invalid part name", drift, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreatePackage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(Manifest),
            ["Assets/payload.txt"] = "legitimate payload"u8.ToArray(),
        };

        foreach ((string relative, byte[] content) in payload)
        {
            File.WriteAllBytes(
                Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)),
                content);
        }

        File.WriteAllText(
            Path.Combine(_root, "AppxBlockMap.xml"),
            PackageBuilder.BlockMapXml(payload),
            Encoding.UTF8);
        return _root;
    }

    private static bool IsCaseSensitive(string directory)
    {
        string lower = Path.Combine(directory, $"case-probe-{Guid.NewGuid():N}");
        string upper = lower.ToUpperInvariant();
        File.WriteAllText(lower, "probe");
        try
        {
            return !File.Exists(upper);
        }
        finally
        {
            File.Delete(lower);
        }
    }

    private sealed class DelegatingOpcPackage(IOpcPackage inner) : IOpcPackage
    {
        public IReadOnlyCollection<string> PartNames => inner.PartNames;

        public string? DetectSnapshotDrift() => inner.DetectSnapshotDrift();

        public bool ContainsPart(string partName) => inner.ContainsPart(partName);

        public Stream OpenPart(string partName) => inner.OpenPart(partName);

        public void Dispose()
        {
        }
    }
}
