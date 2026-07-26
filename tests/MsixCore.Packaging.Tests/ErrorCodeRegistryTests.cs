using System.IO.Compression;
using System.Text;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public sealed class ErrorCodeRegistryTests
{
    [Fact]
    public void MalformedXml_PreservesExactInvalidDataExceptionContract()
    {
        using var stream = Utf8("<Package>");

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(stream));

        Assert.Equal(typeof(InvalidDataException), exception.GetType());
        Assert.Equal(MsixErrorCode.Xml, MsixError.GetCode(exception));
    }

    [Fact]
    public void InvalidManifest_HasManifestSemanticsCode()
    {
        using var stream = Utf8("<Package />");
        AssertCode(
            () => AppxManifestParser.Parse(stream),
            MsixErrorCode.ManifestSemantics);
    }

    [Fact]
    public void InvalidBundleManifest_HasBundleSemanticsCode()
    {
        using var stream = Utf8(
            """<Bundle><Identity Name="Test" Publisher="CN=Test" Version="1.0.0.0" /><Packages /></Bundle>""");
        AssertCode(
            () => BundleManifestParser.Parse(stream),
            MsixErrorCode.BundleSemantics);
    }

    [Fact]
    public void InvalidContentTypesDeclaration_HasContentTypesCode()
    {
        using var stream = Utf8(
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension=".xml" ContentType="application/xml" /></Types>""");
        AssertCode(
            () => ContentTypesParser.Parse(stream),
            MsixErrorCode.ContentTypes);
    }

    [Fact]
    public void InvalidBlockMap_HasBlockMapSemanticsCode()
    {
        using var stream = Utf8("<BlockMap />");
        AssertCode(
            () => BlockMapParser.Parse(stream),
            MsixErrorCode.BlockMapSemantics);
    }

    [Fact]
    public void InvalidSignature_HasSignatureFormatCode()
    {
        byte[] signature = [.. "PKCX"u8, 0x01, 0x02, 0x03];
        AssertCode(
            () => PackageSignatureReader.Read(signature),
            MsixErrorCode.SignatureFormat);
    }

    [Fact]
    public void TraversalPartName_HasPartNameCode()
    {
        using MemoryStream package = CreateZip(("../escape.txt", "payload"));
        AssertCode(
            () => OpcPackage.Open(package),
            MsixErrorCode.PartName);
    }

    [Fact]
    public void MissingManifest_HasFootprintMissingCode()
    {
        using MemoryStream stream = CreateZip(("payload.txt", "payload"));
        using MsixPackage package = MsixPackage.Open(stream, leaveOpen: true);

        AssertCode(
            () => _ = package.Manifest,
            MsixErrorCode.FootprintMissing);
    }

    [Fact]
    public void InvalidCentralDirectory_HasZipStructureCode()
    {
        using MemoryStream valid = CreateZip(("payload.txt", "payload"));
        using var malformed = new MemoryStream([.. valid.ToArray(), .. new byte[16]]);

        AssertCode(
            () => OpcPackage.Open(malformed),
            MsixErrorCode.ZipStructure);
    }

    [Fact]
    public void TryGetCode_IsDefensiveForUncategorizedAndForeignExceptions()
    {
        var plain = new InvalidDataException("x");
        var foreign = new InvalidDataException("x");
        foreign.Data[MsixError.ErrorCodeDataKey] = "not-an-enum";

        AssertNoCode(plain);
        AssertNoCode(foreign);
        AssertNoCode(new IOException("x"));
        AssertNoCode(null);
        Assert.Equal(MsixErrorCode.Unknown, MsixError.GetCode(plain));
    }

    [Fact]
    public void DefaultErrorCode_IsUnknownSoUncategorizedNeverLooksSpecific()
    {
        // Unknown must be the zero value. If a specific category were declared first, the failure
        // path of TryGetCode would hand back that category and an uncategorized exception would be
        // silently misreported as, say, a malformed ZIP.
        Assert.Equal(MsixErrorCode.Unknown, default(MsixErrorCode));
        Assert.Equal(0, (int)MsixErrorCode.Unknown);
    }

    private static void AssertNoCode(Exception? exception)
    {
        Assert.False(MsixError.TryGetCode(exception, out MsixErrorCode actual));
        Assert.Equal(MsixErrorCode.Unknown, actual);
    }

    private static void AssertCode(Action operation, MsixErrorCode expected)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(operation);
        Assert.True(MsixError.TryGetCode(exception, out MsixErrorCode actual));
        Assert.Equal(expected, actual);
    }

    private static MemoryStream Utf8(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static MemoryStream CreateZip(params (string Name, string Content)[] parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in parts)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using Stream destination = entry.Open();
                destination.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
