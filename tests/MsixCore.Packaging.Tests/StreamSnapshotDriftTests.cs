using System.IO.Compression;
using System.Text;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

public sealed class StreamSnapshotDriftTests
{
    private const string Manifest =
        """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Contoso.StreamDrift" Publisher="CN=Contoso" Version="1.0.0.0" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Stream Drift</DisplayName>
            <PublisherDisplayName>Contoso</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    [Fact]
    public void VerifyBlockMap_CallerStreamCentralDirectoryMutatedAfterOpen_IsInvalid()
    {
        using MemoryStream stream = CreatePackageStream();
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        Assert.True(package.VerifyBlockMap().IsValid);

        MutateCentralDirectoryEntryName(stream);

        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.False(result.IsValid);
        Assert.Contains(
            result.CoverageErrors,
            error => error.Contains("caller-supplied stream", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifyBlockMap_NonSeekableCallerStream_FailsClosed()
    {
        using MemoryStream source = CreatePackageStream();
        using var stream = new NonSeekableReadStream(source);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);

        BlockMapVerificationResult result = package.VerifyBlockMap();

        Assert.False(result.IsValid);
        Assert.Contains(
            result.CoverageErrors,
            error => error.Contains("not both readable and seekable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifyCoverage_CallerStreamBecomesUnreadable_FailsClosed()
    {
        using MemoryStream source = CreatePackageStream();
        using var stream = new GatedReadStream(source);
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);
        BlockMap blockMap = package.BlockMap;
        stream.DenyReads = true;

        IReadOnlyList<string> errors = BlockMapVerifier.VerifyCoverage(package.Opc, blockMap);

        Assert.Contains(
            errors,
            error => error.Contains("consistency cannot be established", StringComparison.OrdinalIgnoreCase));
    }

    private static MemoryStream CreatePackageStream()
    {
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(Manifest),
            ["Assets/payload.txt"] = "legitimate payload"u8.ToArray(),
        };
        var parts = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal)
        {
            ["AppxBlockMap.xml"] = Encoding.UTF8.GetBytes(PackageBuilder.BlockMapXml(payload)),
        };

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in parts)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void MutateCentralDirectoryEntryName(MemoryStream stream)
    {
        byte[] bytes = stream.ToArray();
        int header = -1;
        for (int i = bytes.Length - 46; i >= 0; i--)
        {
            if (bytes[i] == (byte)'P'
                && bytes[i + 1] == (byte)'K'
                && bytes[i + 2] == 1
                && bytes[i + 3] == 2)
            {
                header = i;
                break;
            }
        }

        Assert.True(header >= 0, "A ZIP central-directory file header must be present.");
        bytes[header + 46] ^= 0x20;
        long originalPosition = stream.Position;
        stream.Position = 0;
        stream.Write(bytes);
        stream.Position = originalPosition;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class GatedReadStream(Stream inner) : Stream
    {
        public bool DenyReads { get; set; }

        public override bool CanRead => !DenyReads && inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(DenyReads, this);
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(DenyReads, this);
            return inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);
    }
}
