using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Benchmarks;

/// <summary>
/// Synthesizes realistic in-memory / on-disk MSIX (OPC ZIP) packages for benchmarking.
/// Mirrors the block-map construction used by the test-project <c>PackageBuilder</c> so the
/// generated <c>AppxBlockMap.xml</c> matches the payload and passes verification.
/// </summary>
internal static class SyntheticPackage
{
    private const string ManifestTemplate =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="Contoso.BenchApp" Publisher="CN=Contoso" Version="1.2.3.4" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Contoso Bench App</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
            <Logo>Assets\StoreLogo.png</Logo>
          </Properties>
          <Applications>
            <Application Id="App" Executable="App/App.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Bench App" Description="bench" BackgroundColor="#000000"
                                  Square150x150Logo="Assets\Square150.png" Square44x44Logo="Assets\Square44.png" />
            </Application>
          </Applications>
        </Package>
        """;

    /// <summary>Builds the payload part set (excluding block map / signature) for a package.</summary>
    /// <param name="fileCount">Number of synthetic payload files (in addition to the manifest).</param>
    /// <param name="totalPayloadBytes">Approximate total uncompressed size spread across payload files.</param>
    public static Dictionary<string, byte[]> BuildPayload(int fileCount, long totalPayloadBytes)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Encoding.UTF8.GetBytes(ManifestTemplate),
            ["Assets/StoreLogo.png"] = PseudoRandom(4096, seed: 1),
        };

        long perFile = Math.Max(1, totalPayloadBytes / Math.Max(1, fileCount));
        for (int i = 0; i < fileCount; i++)
        {
            // Vary size a little so files span partial and multi-block boundaries (64 KiB blocks).
            long size = perFile + ((i % 7) * 4096);
            parts[$"App/data/file{i:D4}.bin"] = PseudoRandom((int)size, seed: i + 2);
        }

        return parts;
    }

    /// <summary>Serializes the parts (plus a matching block map and optional signature) into a ZIP stream.</summary>
    public static MemoryStream ToZipStream(IReadOnlyDictionary<string, byte[]> payload, bool signed)
    {
        var allParts = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal)
        {
            ["AppxBlockMap.xml"] = Encoding.UTF8.GetBytes(BlockMapXml(payload)),
            ["[Content_Types].xml"] = Encoding.UTF8.GetBytes(ContentTypesXml()),
        };

        if (signed)
        {
            allParts["AppxSignature.p7x"] = BuildSignature();
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in allParts)
            {
                using Stream entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Writes a package to a file and returns its path.</summary>
    public static string ToFile(string directory, string fileName, IReadOnlyDictionary<string, byte[]> payload, bool signed)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        using MemoryStream zip = ToZipStream(payload, signed);
        using FileStream file = File.Create(path);
        zip.CopyTo(file);
        return path;
    }

    /// <summary>Writes a loose (unpacked) package layout to disk and returns the directory path.</summary>
    public static string ToLooseDirectory(string root, IReadOnlyDictionary<string, byte[]> payload)
    {
        Directory.CreateDirectory(root);
        var withBlockMap = new Dictionary<string, byte[]>(payload, StringComparer.Ordinal)
        {
            ["AppxBlockMap.xml"] = Encoding.UTF8.GetBytes(BlockMapXml(payload)),
        };

        foreach ((string name, byte[] content) in withBlockMap)
        {
            string full = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        return root;
    }

    /// <summary>Serializes a matching <c>AppxBlockMap.xml</c> for the given payload parts.</summary>
    public static string BlockMapXml(IReadOnlyDictionary<string, byte[]> parts)
    {
        var sb = new StringBuilder();
        sb.Append("<BlockMap xmlns=\"http://schemas.microsoft.com/appx/2010/blockmap\" ");
        sb.Append("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");
        foreach ((string name, byte[] content) in parts)
        {
            string size = content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("<File Name=\"").Append(name.Replace('/', '\\')).Append("\" Size=\"").Append(size).Append("\" LfhSize=\"0\">");
            for (int offset = 0; offset < content.Length; offset += BlockMap.BlockSize)
            {
                int length = Math.Min(BlockMap.BlockSize, content.Length - offset);
                byte[] hash = SHA256.HashData(content.AsSpan(offset, length));
                sb.Append("<Block Hash=\"").Append(Convert.ToBase64String(hash)).Append("\" />");
            }

            sb.Append("</File>");
        }

        sb.Append("</BlockMap>");
        return sb.ToString();
    }

    private static string ContentTypesXml() =>
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"xml\" ContentType=\"application/vnd.ms-appx.manifest+xml\" />" +
        "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" />" +
        "<Default Extension=\"png\" ContentType=\"image/png\" />" +
        "</Types>";

    private static byte[] BuildSignature()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Contoso Corporation, O=Contoso", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var content = new ContentInfo(Encoding.UTF8.GetBytes("appx-indirect-data"));
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(certificate) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);

        byte[] der = cms.Encode();
        byte[] magic = "PKCX"u8.ToArray();
        byte[] result = new byte[magic.Length + der.Length];
        magic.CopyTo(result, 0);
        der.CopyTo(result, magic.Length);
        return result;
    }

    private static byte[] PseudoRandom(int length, int seed)
    {
        byte[] buffer = new byte[length];
        var rng = new Random(seed);
        rng.NextBytes(buffer);
        return buffer;
    }

    /// <summary>
    /// Extracts every OPC part of a package to <paramref name="destination"/>. This is the same
    /// I/O the deployment engine's package extractor performs; a dedicated
    /// <c>PackageExtractor.Extract</c> API is introduced in a later phase and is not yet present on
    /// this branch, so the harness benchmarks the equivalent part-by-part copy.
    /// </summary>
    public static void ExtractAllParts(IOpcPackage opc, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string part in opc.PartNames)
        {
            string full = Path.Combine(destination, part.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using Stream source = opc.OpenPart(part);
            using FileStream target = File.Create(full);
            source.CopyTo(target);
        }
    }
}
