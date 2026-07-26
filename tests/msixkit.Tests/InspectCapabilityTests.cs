using System.Text.Json;

namespace MsixKit.Tests;

/// <summary>Covers how <c>msixkit inspect</c> reports capability categories (TC-P1-6b/c).</summary>
public class InspectCapabilityTests : IDisposable
{
    private const string Publisher = "CN=Contoso";

    private readonly string _root;

    public InspectCapabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixkit-cap-" + Guid.NewGuid().ToString("N"));
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

    private string CreatePackage()
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.2.3.4" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Contoso My App</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <Capability Name="internetClient" />
                <rescap:Capability Name="runFullTrust" />
                <DeviceCapability Name="location" />
              </Capabilities>
              <Applications>
                <Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />
                </Application>
              </Applications>
            </Package>
            """;

        return LooseCliPackage.Create(_root, Guid.NewGuid().ToString("N"), manifestXml: manifest);
    }

    private static (int Code, string Out) RunInspect(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = InspectCommand.Run(args, output, error);
        return (code, output.ToString());
    }

    [Fact]
    public void Inspect_Text_MarksGatedCapabilitiesAndLeavesGeneralOnesBare()
    {
        (int code, string text) = RunInspect(CreatePackage());

        Assert.Equal(0, code);
        Assert.Contains(
            "Capabilities    : internetClient, runFullTrust (restricted), location (device)",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_Json_ReportsCategorizedCapabilitiesAlongsideTheFlatNames()
    {
        (int code, string json) = RunInspect(CreatePackage(), "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(
            ["internetClient", "runFullTrust", "location"],
            root.GetProperty("Capabilities").EnumerateArray().Select(static c => c.GetString()));

        JsonElement[] declared = [.. root.GetProperty("DeclaredCapabilities").EnumerateArray()];
        Assert.Equal(3, declared.Length);
        Assert.Equal("general", declared[0].GetProperty("Kind").GetString());
        Assert.Equal("restricted", declared[1].GetProperty("Kind").GetString());
        Assert.Equal(
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities",
            declared[1].GetProperty("Namespace").GetString());
        Assert.Equal("device", declared[2].GetProperty("Kind").GetString());
    }
}
