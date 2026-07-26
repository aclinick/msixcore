using System.Text.Json;

namespace MsixKit.Tests;

/// <summary>
/// Covers the manifest validation results that <c>msixkit validate</c> surfaces in text and JSON,
/// and the fact that a semantically invalid manifest fails the CI gate.
/// </summary>
public class ValidateManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "msixkit-validate-manifest-" + Guid.NewGuid().ToString("N"));

    public ValidateManifestTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must not fail the run.
        }

        GC.SuppressFinalize(this);
    }

    private (int Code, string Out) RunValidate(string folder, string manifestXml, params string[] extraArgs)
    {
        string dir = LooseCliPackage.Create(_root, folder, manifestXml: manifestXml);
        var output = new StringWriter();
        var error = new StringWriter();
        int code = ValidateCommand.Run([dir, .. extraArgs], output, error);
        return (code, output.ToString());
    }

    private static string ManifestWithName(string name) => LooseCliPackage.ManifestXml(name: name);

    [Fact]
    public void Validate_ValidManifest_ReportsManifestOk()
    {
        (int code, string output) = RunValidate("manifestOk", LooseCliPackage.ManifestXml());

        Assert.Equal(0, code);
        Assert.Contains("Manifest  : ok", output, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReservedIdentityName_FailsTheGate()
    {
        (int code, string output) = RunValidate("manifestReserved", ManifestWithName("nul"));

        Assert.Equal(1, code);
        Assert.Contains("INTEGRITY FAILED", output, StringComparison.Ordinal);
        Assert.Contains("Manifest  : FAILED (1 error)", output, StringComparison.Ordinal);
        Assert.Contains("manifest: Identity/@Name", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ManifestIssues_AreReportedInJson()
    {
        (int code, string output) = RunValidate("manifestJson", ManifestWithName("nul"), "--json");

        Assert.Equal(1, code);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("ManifestValid").GetBoolean());
        JsonElement issue = Assert.Single(root.GetProperty("ManifestIssues").EnumerateArray());
        Assert.Equal("error", issue.GetProperty("Severity").GetString());
        Assert.Equal("IdentifierReserved", issue.GetProperty("Rule").GetString());
        Assert.Equal("Identity/@Name", issue.GetProperty("Target").GetString());
    }

    [Fact]
    public void Validate_UnknownNamespace_IsAWarningAndStillPasses()
    {
        string manifest =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:future="http://schemas.microsoft.com/appx/manifest/uap/windows10/99">
              <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Contoso My App</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <future:Capability Name="somethingNew" />
              </Capabilities>
            </Package>
            """;

        (int code, string output) = RunValidate("manifestFutureNs", manifest);

        Assert.Equal(0, code);
        Assert.Contains("Manifest  : ok (1 warning)", output, StringComparison.Ordinal);
        Assert.Contains("windows10/99", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ManifestValid_IsTrueInJsonForACleanPackage()
    {
        (int code, string output) = RunValidate("manifestJsonOk", LooseCliPackage.ManifestXml(), "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("ManifestValid").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("ManifestIssues").EnumerateArray());
    }

    [Fact]
    public void Validate_ManifestWithTwoProblems_CountsThemInTheTextSummary()
    {
        string manifest = LooseCliPackage.ManifestXml(name: "nul", publisher: "Contoso");

        (int code, string output) = RunValidate("manifestTwoProblems", manifest);

        Assert.Equal(1, code);
        Assert.Contains("Manifest  : FAILED (2 errors)", output, StringComparison.Ordinal);
    }
}
