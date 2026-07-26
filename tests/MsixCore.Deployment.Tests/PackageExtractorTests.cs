using System.IO.Compression;
using System.Text;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Deployment.Tests;

public class PackageExtractorTests : IDisposable
{
    private readonly string _root;

    public PackageExtractorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static OpcPackage OpcFrom(params (string name, string content)[] parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in parts)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                entry.Write(bytes);
            }
        }

        stream.Position = 0;
        return OpcPackage.Open(stream, leaveOpen: false);
    }

    [Fact]
    public void Extract_WritesAllPartsPreservingHierarchy()
    {
        using OpcPackage package = OpcFrom(
            ("AppxManifest.xml", "<manifest/>"),
            ("Assets/Logo.png", "PNG"),
            ("VFS/ProgramFilesX64/app.exe", "MZ"));
        string dest = Path.Combine(_root, "out");

        PackageExtractor.Extract(package, dest);

        Assert.Equal("<manifest/>", File.ReadAllText(Path.Combine(dest, "AppxManifest.xml")));
        Assert.Equal("PNG", File.ReadAllText(Path.Combine(dest, "Assets", "Logo.png")));
        Assert.Equal("MZ", File.ReadAllText(Path.Combine(dest, "VFS", "ProgramFilesX64", "app.exe")));
    }

    [Fact]
    public void Extract_ReportsProgressToCompletion()
    {
        using OpcPackage package = OpcFrom(
            ("AppxManifest.xml", "<manifest/>"),
            ("a.txt", "1"),
            ("b.txt", "2"),
            ("c.txt", "3"));
        var reported = new List<float>();

        PackageExtractor.Extract(package, Path.Combine(_root, "out"), new Progress<float>(reported.Add));

        // Progress<T> marshals asynchronously; give the posted callbacks a moment to drain.
        SpinWait.SpinUntil(() => reported.Count >= 4, TimeSpan.FromSeconds(2));
        Assert.NotEmpty(reported);
        Assert.Equal(100f, reported[^1], 3);
    }

    [Fact]
    public void Extract_Cancelled_Throws()
    {
        using OpcPackage package = OpcFrom(("AppxManifest.xml", "<manifest/>"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PackageExtractor.Extract(package, Path.Combine(_root, "out"), cancellationToken: cts.Token));
    }

    [Fact]
    public void Extract_PartNameEscapingDestination_ThrowsInvalidData()
    {
        // Defense-in-depth: a package whose part name tries to escape the destination is rejected even
        // if it somehow bypassed OpcPackage's own part-name validation.
        var package = new EscapingOpcPackage();

        Assert.Throws<InvalidDataException>(() =>
            PackageExtractor.Extract(package, Path.Combine(_root, "out")));
    }

    [Fact]
    public void Extract_RootIsReparsePoint_ThrowsInvalidData()
    {
        // A destination root that is itself a symlink/junction redirects every write outside the
        // intended tree even though each part path looks contained, so it must be rejected up front.
        string realTarget = Path.Combine(_root, "real");
        Directory.CreateDirectory(realTarget);
        string link = Path.Combine(_root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, realTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Environment cannot create symlinks (no privilege / Developer Mode); skip.
        }

        using OpcPackage package = OpcFrom(("AppxManifest.xml", "<manifest/>"));

        Assert.Throws<InvalidDataException>(() => PackageExtractor.Extract(package, link));
    }

    [Fact]
    public void Extract_IntermediateSegmentIsDanglingSymlink_ThrowsInvalidData()
    {
        // A symlink whose target does not exist is still a redirect risk. FileInfo.Exists /
        // Directory.Exists report false for such a dangling link, so the guard must detect it via
        // no-follow link metadata regardless of target existence.
        string dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);
        string link = Path.Combine(dest, "Assets");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_root, "does-not-exist"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Environment cannot create symlinks (no privilege / Developer Mode); skip.
        }

        using OpcPackage package = OpcFrom(("AppxManifest.xml", "<manifest/>"), ("Assets/Logo.png", "PNG"));

        Assert.Throws<InvalidDataException>(() => PackageExtractor.Extract(package, dest));
    }

    [Fact]
    public void ExtractAndVerify_ReadsEachPayloadPartOnce()
    {
        string packagePath = PackedMsixBuilder.Create(
            _root,
            "single-read.msix",
            extraParts: new Dictionary<string, byte[]>
            {
                ["Assets/Logo.png"] = new byte[BlockMap.BlockSize + 17],
            });
        using MsixPackage package = MsixPackage.Open(packagePath);
        BlockMap blockMap = package.BlockMap;
        using var counting = new CountingOpcPackage(package.Opc);

        BlockMapVerificationResult result = PackageExtractor.ExtractAndVerify(
            counting,
            blockMap,
            Path.Combine(_root, "verified"));

        Assert.True(result.IsValid);
        Assert.Equal(1, counting.OpenCounts[OpcPartNames.AppxManifest]);
        Assert.Equal(1, counting.OpenCounts["Assets/Logo.png"]);
        Assert.All(blockMap.Files, mapped => Assert.Contains(result.Files, result => result.Name == mapped.Name));
    }

    [Fact]
    public void ExtractAndVerify_DirectoryMutatedAfterOpen_RejectsBeforeCopy()
    {
        string packagePath = PackedMsixBuilder.Create(
            _root,
            "drift-source.msix",
            extraParts: new Dictionary<string, byte[]>
            {
                ["Assets/Logo.png"] = "legitimate"u8.ToArray(),
            });
        string looseDirectory = Path.Combine(_root, "drift-loose");
        using (MsixPackage packed = MsixPackage.Open(packagePath))
        {
            PackageExtractor.Extract(packed.Opc, looseDirectory);
        }

        using MsixPackage loose = MsixPackage.OpenDirectory(looseDirectory);
        File.WriteAllText(Path.Combine(looseDirectory, "unmapped.txt"), "attacker");
        string destination = Path.Combine(_root, "drift-output");

        using var decorated = new CountingOpcPackage(loose.Opc);
        BlockMapVerificationResult result = PackageExtractor.ExtractAndVerify(
            decorated,
            loose.BlockMap,
            destination);

        Assert.False(result.IsValid);
        string driftError = Assert.Single(
            result.CoverageErrors,
            error => error.Contains("Package snapshot drift detected", StringComparison.Ordinal));
        Assert.Contains("unmapped.txt", driftError);
        Assert.False(Directory.Exists(destination));
    }

    /// <summary>A hostile <see cref="IOpcPackage"/> that returns a traversing part name.</summary>
    private sealed class EscapingOpcPackage : IOpcPackage
    {
        public IReadOnlyCollection<string> PartNames => new[] { "../escape.txt" };

        // This hostile test double has a fixed in-memory part set and no mutable backing namespace.
        public string? DetectSnapshotDrift() => null;

        public OpcPartZipInfo? GetZipInfo(string partName) => null;

        public bool ContainsPart(string partName) => true;

        public Stream OpenPart(string partName) => new MemoryStream(Encoding.UTF8.GetBytes("x"));

        public void Dispose()
        {
        }
    }

    private sealed class CountingOpcPackage(IOpcPackage inner) : IOpcPackage
    {
        public Dictionary<string, int> OpenCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> PartNames => inner.PartNames;

        public string? DetectSnapshotDrift() => inner.DetectSnapshotDrift();

        public OpcPartZipInfo? GetZipInfo(string partName) => inner.GetZipInfo(partName);

        public bool ContainsPart(string partName) => inner.ContainsPart(partName);

        public Stream OpenPart(string partName)
        {
            OpenCounts[partName] = OpenCounts.GetValueOrDefault(partName) + 1;
            return inner.OpenPart(partName);
        }

        public void Dispose()
        {
        }
    }
}
