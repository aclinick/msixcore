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
        Assert.Contains("VALID", output);
        Assert.Contains("Block map : ok", output);
    }

    [Fact]
    public void Validate_CorruptBlockMap_ReturnsOneAndReportsInvalid()
    {
        string dir = LooseCliPackage.Create(_root, "pkgCorrupt");
        LooseCliPackage.CorruptBlockMap(dir);

        (int code, string output, _) = RunValidate(dir);

        Assert.Equal(1, code);
        Assert.Contains("INVALID", output);
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

    [Fact]
    public void Program_Help_ReturnsZeroAndListsVerbs()
    {
        int code = Program.Main(["--help"]);
        Assert.Equal(0, code);
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
