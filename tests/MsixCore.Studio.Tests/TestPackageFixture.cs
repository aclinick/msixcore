using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MsixCore.Packaging.Opc;

namespace MsixCore.Studio.Tests;

internal sealed class TestPackageFixture : IDisposable
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6];

    private static readonly byte[] Manifest = Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="Contoso.StudioTest" Publisher="CN=Contoso" Version="2.3.4.5" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Studio Test</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
          <Applications>
            <Application Id="App" Executable="StudioTest.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Studio Test App" Description="Test application" />
            </Application>
          </Applications>
          <Capabilities>
            <Capability Name="internetClient" />
            <Capability Name="runFullTrust" />
          </Capabilities>
        </Package>
        """);

    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"studio-test-fixtures-{Guid.NewGuid():N}");

    public string CreatePackageFile(bool signed, out SignatureExpectation? signature)
    {
        Directory.CreateDirectory(_root);
        Dictionary<string, byte[]> parts = CreateParts(signed, out signature);
        string path = Path.Combine(_root, signed ? "signed.msix" : "unsigned.msix");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, byte[] content) in parts)
        {
            using Stream entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
            entry.Write(content);
        }

        return path;
    }

    public string CreateLooseDirectory(bool tampered)
    {
        string path = Path.Combine(_root, tampered ? "loose-tampered" : "loose-valid");
        Directory.CreateDirectory(path);
        Dictionary<string, byte[]> parts = CreateParts(signed: false, out _);
        foreach ((string name, byte[] content) in parts)
        {
            string filePath = Path.Combine(path, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, content);
        }

        if (tampered)
        {
            File.WriteAllBytes(Path.Combine(path, "payload.bin"), [9, 9, 9, 9, 9, 9]);
        }

        return path;
    }

    public string CreateLooseDirectoryWithCoverageError()
    {
        string path = Path.Combine(_root, "loose-coverage-error");
        Directory.CreateDirectory(path);
        Dictionary<string, byte[]> parts = CreateParts(signed: false, out _);
        parts["unlisted.bin"] = [7, 8, 9];
        foreach ((string name, byte[] content) in parts)
        {
            string filePath = Path.Combine(path, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, content);
        }

        return path;
    }

    public string CreateNonPackageFile()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "not-a-package.txt");
        File.WriteAllText(path, "This is not an MSIX package.");
        return path;
    }

    public string MissingPackagePath => Path.Combine(_root, "missing.msix");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Dictionary<string, byte[]> CreateParts(
        bool signed,
        out SignatureExpectation? signatureExpectation)
    {
        var payloadParts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = Manifest,
            ["payload.bin"] = Payload,
        };
        var parts = new Dictionary<string, byte[]>(payloadParts, StringComparer.Ordinal)
        {
            ["AppxBlockMap.xml"] = BuildBlockMap(payloadParts),
            [OpcPartNames.ContentTypes] =
                """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Default Extension="bin" ContentType="application/octet-stream"/><Default Extension="p7x" ContentType="application/vnd.ms-appx.signature"/></Types>"""u8.ToArray(),
        };

        if (signed)
        {
            using X509Certificate2 certificate = CreateCertificate();
            parts["AppxSignature.p7x"] = BuildSignature(certificate);
            signatureExpectation = new SignatureExpectation(
                certificate.SubjectName.Name,
                certificate.IssuerName.Name,
                certificate.Thumbprint,
                certificate.NotBefore,
                certificate.NotAfter);
        }
        else
        {
            signatureExpectation = null;
        }

        return parts;
    }

    private static byte[] BuildBlockMap(IReadOnlyDictionary<string, byte[]> parts)
    {
        var builder = new StringBuilder(
            """<BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">""");

        foreach ((string name, byte[] content) in parts)
        {
            builder.Append("<File Name=\"")
                .Append(name.Replace('/', '\\'))
                .Append("\" Size=\"")
                .Append(content.Length.ToString(CultureInfo.InvariantCulture))
                .Append("\" LfhSize=\"0\">");

            for (int offset = 0; offset < content.Length; offset += 64 * 1024)
            {
                int length = Math.Min(64 * 1024, content.Length - offset);
                string hash = Convert.ToBase64String(SHA256.HashData(content.AsSpan(offset, length)));
                builder.Append("<Block Hash=\"").Append(hash).Append("\" />");
            }

            builder.Append("</File>");
        }

        builder.Append("</BlockMap>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static X509Certificate2 CreateCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Contoso",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
    }

    private static byte[] BuildSignature(X509Certificate2 certificate)
    {
        var content = new ContentInfo(Encoding.UTF8.GetBytes("appx-indirect-data"));
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(signer);

        byte[] encoded = cms.Encode();
        byte[] signature = new byte[4 + encoded.Length];
        "PKCX"u8.CopyTo(signature);
        encoded.CopyTo(signature, 4);
        return signature;
    }
}

internal sealed record SignatureExpectation(
    string SubjectName,
    string IssuerName,
    string Thumbprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
