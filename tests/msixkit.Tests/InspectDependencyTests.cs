using System.Text.Json;

namespace MsixKit.Tests;

/// <summary>Covers how <c>msixkit inspect</c> reports declared package dependencies.</summary>
public class InspectDependencyTests : IDisposable
{
    private const string Publisher = "CN=Contoso";

    private readonly string _root;

    public InspectDependencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixkit-deps-" + Guid.NewGuid().ToString("N"));
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

    private string CreatePackage(string? dependencies)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap4="http://schemas.microsoft.com/appx/manifest/uap/windows10/4">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.2.3.4" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Contoso My App</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
              </Properties>{(dependencies is null ? "" : $"\n  <Dependencies>\n    {dependencies}\n  </Dependencies>")}
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
    public void Inspect_ListsDeclaredDependencies()
    {
        string dir = CreatePackage(
            $"""
            <PackageDependency Name="Microsoft.VCLibs.140.00" MinVersion="14.0.30704.0" Publisher="{Publisher}" />
                <uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />
            """);

        (int code, string output) = RunInspect(dir);

        Assert.Equal(0, code);
        Assert.Contains("Dependencies    :", output, StringComparison.Ordinal);
        Assert.Contains("framework   Microsoft.VCLibs.140.00 >= 14.0.30704.0", output, StringComparison.Ordinal);
        Assert.Contains("mainPackage Contoso.MainApp", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_WithNoDependencies_OmitsTheSection()
    {
        (int code, string output) = RunInspect(CreatePackage(null));

        Assert.Equal(0, code);
        Assert.DoesNotContain("Dependencies", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_Json_EmitsDependencies()
    {
        string dir = CreatePackage(
            $"""<PackageDependency Name="Microsoft.VCLibs.140.00" MinVersion="14.0.30704.0" Publisher="{Publisher}" MaxMajorVersionTested="15" />""");

        (int code, string output) = RunInspect(dir, "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement dependency = Assert.Single(document.RootElement.GetProperty("Dependencies").EnumerateArray());
        Assert.Equal("framework", dependency.GetProperty("Kind").GetString());
        Assert.Equal("Microsoft.VCLibs.140.00", dependency.GetProperty("Name").GetString());
        Assert.Equal("14.0.30704.0", dependency.GetProperty("MinVersion").GetString());
        Assert.Equal(15, dependency.GetProperty("MaxMajorVersionTested").GetInt32());
    }

    [Fact]
    public void Inspect_Json_WithNoDependencies_EmitsAnEmptyArray()
    {
        (int code, string output) = RunInspect(CreatePackage(null), "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Empty(document.RootElement.GetProperty("Dependencies").EnumerateArray());
    }
}
