using System.Text;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

public class AppxManifestParserTests
{
    private const string SampleManifest =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Package
          xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
          xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
          xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
          xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10">
          <Identity Name="Contoso.MyApp"
                    Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
                    Version="1.2.3.4"
                    ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>Contoso My App</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
            <Description>A sample app.</Description>
            <Logo>Assets\StoreLogo.png</Logo>
          </Properties>
          <Dependencies>
            <TargetDeviceFamily Name="MSIXCore.Desktop" MinVersion="6.1.7601.0" MaxVersionTested="10.0.10240.0" />
            <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.16299.0" MaxVersionTested="10.0.18362.0" />
          </Dependencies>
          <Capabilities>
            <Capability Name="internetClient" />
            <rescap:Capability Name="runFullTrust" />
            <DeviceCapability Name="location" />
          </Capabilities>
          <Applications>
            <Application Id="App" Executable="MyApp.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="My App"
                                  Description="The app"
                                  Square150x150Logo="Assets\Square150.png"
                                  Square44x44Logo="Assets\Square44.png"
                                  BackgroundColor="#0078D7" />
            </Application>
          </Applications>
        </Package>
        """;

    private static AppxManifest ParseSample() =>
        AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(SampleManifest)));

    [Fact]
    public void Parse_ReadsIdentity()
    {
        AppxManifest manifest = ParseSample();

        Assert.Equal("Contoso.MyApp", manifest.Identity.Name);
        Assert.Equal(new Version(1, 2, 3, 4), manifest.Identity.Version);
        Assert.Equal(ProcessorArchitecture.X64, manifest.Identity.Architecture);
        Assert.Equal("Contoso.MyApp_8wekyb3d8bbwe", manifest.Identity.PackageFamilyName);
    }

    [Fact]
    public void Parse_ReadsProperties()
    {
        AppxManifest manifest = ParseSample();

        Assert.Equal("Contoso My App", manifest.DisplayName);
        Assert.Equal("Contoso Ltd", manifest.PublisherDisplayName);
        Assert.Equal("A sample app.", manifest.Description);
        Assert.Equal(@"Assets\StoreLogo.png", manifest.Logo);
        Assert.False(manifest.IsFramework);
    }

    [Fact]
    public void Parse_ReadsCapabilities_AcrossNamespaces()
    {
        AppxManifest manifest = ParseSample();

        Assert.Equal(["internetClient", "runFullTrust", "location"], manifest.Capabilities);
    }

    [Fact]
    public void Parse_ReadsTargetDeviceFamilies()
    {
        AppxManifest manifest = ParseSample();

        Assert.Equal(2, manifest.TargetDeviceFamilies.Count);
        TargetDeviceFamily core = manifest.TargetDeviceFamilies[0];
        Assert.Equal("MSIXCore.Desktop", core.Name);
        Assert.Equal(new Version(6, 1, 7601, 0), core.MinVersion);
        Assert.Equal(new Version(10, 0, 10240, 0), core.MaxVersionTested);
    }

    [Fact]
    public void Parse_ReadsApplicationAndVisualElements()
    {
        AppxManifest manifest = ParseSample();

        ManifestApplication app = Assert.Single(manifest.Applications);
        Assert.Equal("App", app.Id);
        Assert.Equal("MyApp.exe", app.Executable);
        Assert.Equal("Windows.FullTrustApplication", app.EntryPoint);
        Assert.Equal("My App", app.VisualElements.DisplayName);
        Assert.Equal(@"Assets\Square150.png", app.VisualElements.Square150x150Logo);
        Assert.Equal("#0078D7", app.VisualElements.BackgroundColor);
        Assert.True(app.VisualElements.AppListEntry);
    }

    [Theory]
    [InlineData("x86", ProcessorArchitecture.X86)]
    [InlineData("X64", ProcessorArchitecture.X64)]
    [InlineData("arm64", ProcessorArchitecture.Arm64)]
    [InlineData("neutral", ProcessorArchitecture.Neutral)]
    [InlineData(null, ProcessorArchitecture.Neutral)]
    [InlineData("weird", ProcessorArchitecture.Unknown)]
    public void ParseArchitecture_MapsValues(string? input, ProcessorArchitecture expected)
    {
        Assert.Equal(expected, AppxManifestParser.ParseArchitecture(input));
    }

    [Fact]
    public void Parse_MissingIdentity_Throws()
    {
        const string xml = """<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Properties/></Package>""";
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3")]
    [InlineData("65536.0.0.0")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.-2.3.4")]
    public void Parse_NonQuadVersion_Throws(string version)
    {
        string xml =
            $"""
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="a" Publisher="CN=a" Version="{version}" />
            </Package>
            """;
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_FrameworkNumericBoolean_IsHonored()
    {
        const string xml =
            """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="a" Publisher="CN=a" Version="1.0.0.0" />
              <Properties><Framework>1</Framework></Properties>
            </Package>
            """;
        AppxManifest manifest = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        Assert.True(manifest.IsFramework);
    }

    [Fact]
    public void Parse_TargetDeviceFamilyMissingMaxVersionTested_Throws()
    {
        const string xml =
            """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="a" Publisher="CN=a" Version="1.0.0.0" />
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.16299.0" />
              </Dependencies>
            </Package>
            """;
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_WrongRoot_Throws()
    {
        const string xml = """<NotAPackage/>""";
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_MalformedXml_Throws()
    {
        const string xml = "<Package><Identity";
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Fact]
    public void Parse_RejectsDtd_ForXxeSafety()
    {
        const string xml =
            """
            <?xml version="1.0"?>
            <!DOCTYPE Package [ <!ENTITY xxe "boom"> ]>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="a" Publisher="CN=a" Version="1.0.0.0" />
            </Package>
            """;
        Assert.Throws<InvalidDataException>(() => AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }
}
