using System.Text;
using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

public class BundleManifestParserTests
{
    private const string SampleBundle =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
          <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" />
          <Packages>
            <Package Type="application" Version="1.2.3.4" Architecture="x64" FileName="MyApp_x64.msix" Offset="0" Size="100" />
            <Package Type="application" Version="1.2.3.4" Architecture="arm64" FileName="MyApp_arm64.msix" />
            <Package Type="resource" Version="1.2.3.4" ResourceId="en-us" FileName="MyApp_language-en.msix">
              <Resources>
                <Resource Language="en-US" />
              </Resources>
            </Package>
            <Package Version="not-a-version" FileName="broken.msix" />
          </Packages>
        </Bundle>
        """;

    private static BundleManifest ParseSample() =>
        BundleManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(SampleBundle)));

    [Fact]
    public void Parse_ReadsIdentity()
    {
        BundleManifest bundle = ParseSample();

        Assert.Equal("Contoso.MyApp", bundle.Identity.Name);
        Assert.Equal(new Version(1, 2, 3, 4), bundle.Identity.Version);
        Assert.Equal(ProcessorArchitecture.Neutral, bundle.Identity.Architecture);
    }

    [Fact]
    public void Parse_SkipsMalformedPackageEntries()
    {
        BundleManifest bundle = ParseSample();

        Assert.Equal(3, bundle.Packages.Count);
    }

    [Fact]
    public void Parse_ReadsApplicationPackage()
    {
        BundleManifest bundle = ParseSample();

        BundlePackageEntry app = bundle.Packages[0];
        Assert.Equal("MyApp_x64.msix", app.FileName);
        Assert.Equal(BundlePackageType.Application, app.Type);
        Assert.Equal(ProcessorArchitecture.X64, app.Architecture);
    }

    [Fact]
    public void Parse_ReadsResourcePackage()
    {
        BundleManifest bundle = ParseSample();

        BundlePackageEntry resource = bundle.Packages[2];
        Assert.Equal(BundlePackageType.Resource, resource.Type);
        Assert.Equal("en-us", resource.ResourceId);
        Assert.Equal(["en-US"], resource.Resources);
    }

    [Fact]
    public void Parse_WrongRoot_Throws()
    {
        const string xml = """<Package xmlns="http://schemas.microsoft.com/appx/2013/bundle" />""";
        Assert.Throws<InvalidDataException>(
            () => BundleManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_MissingIdentity_Throws()
    {
        const string xml = """<Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle"><Packages/></Bundle>""";
        Assert.Throws<InvalidDataException>(
            () => BundleManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_RejectsDtd()
    {
        const string xml =
            """
            <?xml version="1.0"?>
            <!DOCTYPE Bundle [ <!ENTITY xxe "boom"> ]>
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="a" Publisher="CN=a" Version="1.0.0.0" />
            </Bundle>
            """;
        Assert.Throws<InvalidDataException>(
            () => BundleManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }
}
