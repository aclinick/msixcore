using System.Text;

namespace MsixMgr.Tests;

public class CliCommandTests : IDisposable
{
    private readonly string _root;

    public CliCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixmgr-cli-" + Guid.NewGuid().ToString("N"));
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

    private static (int Code, string Out, string Err) RunInspect(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = InspectCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out, string Err) RunValidate(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = ValidateCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Inspect_LoosePackage_PrintsIdentity()
    {
        string dir = LooseCliPackage.Create(_root, "pkgA");

        (int code, string output, _) = RunInspect(dir);

        Assert.Equal(0, code);
        Assert.Contains("Contoso.MyApp", output);
        Assert.Contains("Contoso My App", output);
        Assert.Contains("Contoso.MyApp_1.2.3.4_x64__", output);
    }

    [Fact]
    public void Inspect_Json_EmitsMachineReadableReport()
    {
        string dir = LooseCliPackage.Create(_root, "pkgJson");

        (int code, string output, _) = RunInspect(dir, "--json");

        Assert.Equal(0, code);
        Assert.Contains("\"Name\": \"Contoso.MyApp\"", output);
        Assert.Contains("\"PackageFamilyName\"", output);
    }

    [Fact]
    public void Inspect_MissingPath_ReturnsUsageError()
    {
        (int code, _, string err) = RunInspect(Path.Combine(_root, "nope"));

        Assert.Equal(1, code);
        Assert.Contains("msixmgr inspect", err);
    }

    [Fact]
    public void Inspect_NoArgs_ReturnsExitCode2()
    {
        (int code, _, string err) = RunInspect();

        Assert.Equal(2, code);
        Assert.Contains("path is required", err);
    }

    [Fact]
    public void Validate_ValidLoosePackage_ReturnsZero()
    {
        var extra = new Dictionary<string, byte[]>
        {
            ["Assets/data.bin"] = Encoding.UTF8.GetBytes("hello world payload"),
        };
        string dir = LooseCliPackage.Create(_root, "pkgValid", extra);

        (int code, string output, _) = RunValidate(dir);

        Assert.Equal(0, code);
        Assert.Contains("INTEGRITY OK", output);
        Assert.Contains("Block map : ok", output);
        Assert.Contains("note:", output);
    }

    [Fact]
    public void Validate_CorruptBlockMap_ReturnsOneAndReportsInvalid()
    {
        string dir = LooseCliPackage.Create(_root, "pkgCorrupt");
        LooseCliPackage.CorruptBlockMap(dir);

        (int code, string output, _) = RunValidate(dir);

        Assert.Equal(1, code);
        Assert.Contains("INTEGRITY FAILED", output);
    }

    [Fact]
    public void Validate_Json_ReportsValidity()
    {
        string dir = LooseCliPackage.Create(_root, "pkgVJson");

        (int code, string output, _) = RunValidate(dir, "--json");

        Assert.Equal(0, code);
        Assert.Contains("\"IsValid\": true", output);
        Assert.Contains("\"BlockMapValid\": true", output);
    }

    private static (int Code, string Out, string Err) RunUnpack(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = UnpackCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Unpack_LoosePackage_ExtractsAllParts()
    {
        var extra = new Dictionary<string, byte[]>
        {
            ["Assets/data.bin"] = Encoding.UTF8.GetBytes("payload"),
        };
        string dir = LooseCliPackage.Create(_root, "pkgUnpack", extra);
        string dest = Path.Combine(_root, "unpacked");

        (int code, string output, _) = RunUnpack(dir, "-Destination", dest);

        Assert.Equal(0, code);
        Assert.Contains("Extracted", output);
        Assert.True(File.Exists(Path.Combine(dest, "AppxManifest.xml")));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(dest, "Assets", "data.bin")));
    }

    [Fact]
    public void Unpack_Json_EmitsReport()
    {
        string dir = LooseCliPackage.Create(_root, "pkgUnpackJson");
        string dest = Path.Combine(_root, "unpacked-json");

        (int code, string output, _) = RunUnpack(dir, "-Destination", dest, "--json");

        Assert.Equal(0, code);
        Assert.Contains("\"ExtractedPartCount\"", output);
        Assert.Contains("\"Destination\"", output);
    }

    [Fact]
    public void Unpack_MissingDestination_ReturnsUsageError()
    {
        string dir = LooseCliPackage.Create(_root, "pkgNoDest");

        (int code, _, string err) = RunUnpack(dir);

        Assert.Equal(2, code);
        Assert.Contains("destination directory is required", err);
    }

    [Fact]
    public void Unpack_MissingPath_ReturnsUsageError()
    {
        (int code, _, string err) = RunUnpack("-Destination", Path.Combine(_root, "d"));

        Assert.Equal(2, code);
        Assert.Contains("package path is required", err);
    }

    [Fact]
    public void Program_HelpAndDispatchUseTheSameVerbRegistry()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = Program.Run(["--help"], output, error);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());

        string help = output.ToString();
        string verbSection = help.Split("Verbs:", StringSplitOptions.None)[1]
            .Split("<path> may be", StringSplitOptions.None)[0];
        string[] helpVerbs = verbSection
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimStart().Split(' ', 2)[0])
            .ToArray();
        string[] registeredVerbs = Program.Verbs.Select(static verb => verb.Name).ToArray();

        Assert.Equal(registeredVerbs, helpVerbs);

        foreach (string verb in helpVerbs)
        {
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();

            int dispatchCode = Program.Run([verb], output, error);

            Assert.Equal(2, dispatchCode);
            Assert.Contains($"msixmgr {verb}:", error.ToString());
            Assert.DoesNotContain("unknown verb", error.ToString());
        }
    }

    [Fact]
    public void Program_UnknownVerb_ReturnsTwo()
    {
        int code = Program.Main(["frobnicate"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public void Program_Version_ReturnsZero()
    {
        int code = Program.Main(["--version"]);
        Assert.Equal(0, code);
    }
}
