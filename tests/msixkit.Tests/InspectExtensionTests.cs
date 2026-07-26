using System.Text.Json;

namespace MsixKit.Tests;

/// <summary>Covers how <c>msixkit inspect</c> reports declared extensions (TC-P1-4g).</summary>
public class InspectExtensionTests : IDisposable
{
    private const string Publisher = "CN=Contoso";

    private readonly string _root;

    public InspectExtensionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixkit-ext-" + Guid.NewGuid().ToString("N"));
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

    private string CreatePackage(string applicationExtensions, string packageExtensions = "")
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
                     xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
                     xmlns:desktop7="http://schemas.microsoft.com/appx/manifest/desktop/windows10/7"
                     xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.2.3.4" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Contoso My App</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
              </Properties>
              <Applications>
                <Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />
            {applicationExtensions}
                </Application>
              </Applications>
            {packageExtensions}
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

    private const string SeveralExtensions =
        """
        <Extensions>
          <uap:Extension Category="windows.fileTypeAssociation">
            <uap:FileTypeAssociation Name="contoso-doc">
              <uap:SupportedFileTypes>
                <uap:FileType>.cdoc</uap:FileType>
                <uap:FileType>.cdx</uap:FileType>
              </uap:SupportedFileTypes>
            </uap:FileTypeAssociation>
          </uap:Extension>
          <uap:Extension Category="windows.protocol">
            <uap:Protocol Name="myscheme" />
          </uap:Extension>
          <uap5:Extension Category="windows.appExecutionAlias">
            <uap5:AppExecutionAlias>
              <uap5:ExecutionAlias Alias="contoso.exe" />
            </uap5:AppExecutionAlias>
          </uap5:Extension>
          <desktop:Extension Category="windows.startupTask" Executable="App.exe">
            <desktop:StartupTask TaskId="ContosoStartup" Enabled="true" />
          </desktop:Extension>
          <desktop:Extension Category="windows.fullTrustProcess" Executable="Helper.exe" />
          <com:Extension Category="windows.comServer">
            <com:ComServer>
              <com:ExeServer Executable="Server.exe">
                <com:Class Id="8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21" />
              </com:ExeServer>
            </com:ComServer>
          </com:Extension>
        </Extensions>
        """;

    [Fact]
    public void Inspect_ListsEveryDeclaredExtension()
    {
        (int code, string output) = RunInspect(CreatePackage(SeveralExtensions));

        Assert.Equal(0, code);
        Assert.Contains("Extensions      :", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.fileTypeAssociation contoso-doc: .cdoc .cdx", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.protocol myscheme:", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.appExecutionAlias contoso.exe", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.startupTask ContosoStartup (enabled=True)", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.fullTrustProcess", output, StringComparison.Ordinal);
        Assert.Contains("[App] windows.comServer 8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_Json_EmitsEveryDeclaredExtension()
    {
        (int code, string output) = RunInspect(CreatePackage(SeveralExtensions), "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] extensions = document.RootElement.GetProperty("Extensions").EnumerateArray().ToArray();

        Assert.Equal(
            [
                "windows.fileTypeAssociation",
                "windows.protocol",
                "windows.appExecutionAlias",
                "windows.startupTask",
                "windows.fullTrustProcess",
                "windows.comServer",
            ],
            extensions.Select(e => e.GetProperty("Category").GetString()));
        Assert.All(extensions, e => Assert.Equal("App", e.GetProperty("ApplicationId").GetString()));
        Assert.Equal(".cdoc .cdx", extensions[0].GetProperty("Details").GetString()!.Split(": ")[1]);
        Assert.Equal("Helper.exe", extensions[4].GetProperty("Executable").GetString());
    }

    [Fact]
    public void Inspect_Json_ReportsAPackageLevelExtensionWithoutAnApplicationId()
    {
        string dir = CreatePackage(
            applicationExtensions: "",
            packageExtensions:
                """
                <Extensions>
                  <desktop7:Extension Category="windows.shortcut">
                    <desktop7:Shortcut File="Contoso.lnk" Icon="icon.ico" />
                  </desktop7:Extension>
                </Extensions>
                """);

        (int code, string output) = RunInspect(dir, "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement extension = Assert.Single(document.RootElement.GetProperty("Extensions").EnumerateArray());
        Assert.Equal("windows.shortcut", extension.GetProperty("Category").GetString());
        // Serialization omits nulls, so an absent ApplicationId is how "package-level" is expressed.
        Assert.False(extension.TryGetProperty("ApplicationId", out _));
    }

    [Fact]
    public void Inspect_ReportsAPackageLevelExtensionAgainstThePackage()
    {
        string dir = CreatePackage(
            applicationExtensions: "",
            packageExtensions:
                """
                <Extensions>
                  <desktop7:Extension Category="windows.shortcut">
                    <desktop7:Shortcut File="Contoso.lnk" Icon="icon.ico" />
                  </desktop7:Extension>
                </Extensions>
                """);

        (int code, string output) = RunInspect(dir);

        Assert.Equal(0, code);
        Assert.Contains("[package] windows.shortcut Contoso.lnk", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_WithNoExtensions_OmitsTheSection()
    {
        (int code, string output) = RunInspect(CreatePackage(""));

        Assert.Equal(0, code);
        Assert.DoesNotContain("Extensions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_Json_WithNoExtensions_EmitsAnEmptyArray()
    {
        (int code, string output) = RunInspect(CreatePackage(""), "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("Extensions").EnumerateArray());
    }
}
