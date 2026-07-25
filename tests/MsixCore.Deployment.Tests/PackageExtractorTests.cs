using System.IO.Compression;
using System.Text;
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

    /// <summary>A hostile <see cref="IOpcPackage"/> that returns a traversing part name.</summary>
    private sealed class EscapingOpcPackage : IOpcPackage
    {
        public IReadOnlyCollection<string> PartNames => new[] { "../escape.txt" };

        public bool ContainsPart(string partName) => true;

        public Stream OpenPart(string partName) => new MemoryStream(Encoding.UTF8.GetBytes("x"));

        public void Dispose()
        {
        }
    }
}
