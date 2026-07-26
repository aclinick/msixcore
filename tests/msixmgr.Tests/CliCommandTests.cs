using System.Text;
using System.Text.Json;
using MsixCore.Deployment;
using MsixCore.Packaging;

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

    private static (int Code, string Out, string Err) RunPack(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = PackCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out, string Err) RunBundle(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = BundleCommand.Run(args, o, e);
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
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("\"Name\": \"Contoso.MyApp\"", output);
        Assert.Contains("\"PackageFamilyName\"", output);
    }

    [Fact]
    public void Inspect_MissingPath_ReturnsOperationalError()
    {
        (int code, _, string err) = RunInspect(Path.Combine(_root, "nope"));

        Assert.Equal(3, code);
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
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("\"IsValid\": true", output);
        Assert.Contains("\"BlockMapValid\": true", output);
    }

    #region Real-signed fixture validation (exit codes + attack signature)

    private static string RealSignedFixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealSigned", name);

    [Fact]
    public void Validate_RealSignedPackage_ExitsZero()
    {
        (int code, string output, _) = RunValidate(RealSignedFixture("SignTest.msix"));

        Assert.Equal(0, code);
        Assert.Contains("INTEGRITY OK", output);
    }

    [Fact]
    public void Validate_StapledPackage_ExitsNonZero()
    {
        (int code, string output, _) = RunValidate(RealSignedFixture("Stapled.msix"));

        Assert.NotEqual(0, code);
        Assert.Contains("INTEGRITY FAILED", output);
    }

    [Fact]
    public void Validate_StapledPackage_Json_ShowsAttackSignature()
    {
        // The pairing "CMS valid + binding invalid" is precisely the attack signature
        // for a stolen-signature stapling attack. This must never regress.
        (int code, string output, _) = RunValidate(RealSignedFixture("Stapled.msix"), "--json");

        Assert.NotEqual(0, code);

        using JsonDocument doc = JsonDocument.Parse(output);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("IsValid").GetBoolean());
        Assert.True(root.GetProperty("CmsIntegrityValid").GetBoolean());
        Assert.False(root.GetProperty("SignatureBindingVerified").GetBoolean());
    }

    [Fact]
    public void Validate_SignedDirectoryMutatedAfterOpen_ReportsDriftCauseWithoutAxci()
    {
        string directory = Path.Combine(_root, "signed-directory-drift");
        using (MsixPackage packed = MsixPackage.Open(RealSignedFixture("SignTest.msix")))
        {
            PackageExtractor.Extract(packed.Opc, directory);
        }

        using MsixPackage loose = MsixPackage.OpenDirectory(directory);
        Assert.True(loose.IsSigned);
        File.WriteAllText(Path.Combine(directory, "evil.dll"), "attacker");

        ValidationReport report = ValidateCommand.Validate(loose);

        Assert.False(report.IsValid);
        string bindingError = Assert.Single(
            report.Errors,
            error => error.StartsWith("signature: APPX indirect-data binding FAILED", StringComparison.Ordinal));
        Assert.Contains("snapshot drift detected", bindingError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            report.Errors,
            error => error.Contains("AXCI", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Directory payload-set integrity (extra/missing files fail validate)

    [Fact]
    public void Validate_DirectoryWithExtraFile_ExitsNonZero()
    {
        // A file not covered by the block map must cause validation failure.
        var extra = new Dictionary<string, byte[]>
        {
            ["Assets/data.bin"] = Encoding.UTF8.GetBytes("legit payload"),
        };
        string dir = LooseCliPackage.Create(_root, "pkgExtra", extra);

        // Add a file not in the block map.
        File.WriteAllBytes(Path.Combine(dir, "evil.dll"), new byte[] { 0x4D, 0x5A });

        (int code, string output, _) = RunValidate(dir);

        Assert.NotEqual(0, code);
        Assert.Contains("INTEGRITY FAILED", output);
    }

    [Fact]
    public void Validate_DirectoryWithMissingPayload_ExitsNonZero()
    {
        var extra = new Dictionary<string, byte[]>
        {
            ["Assets/data.bin"] = Encoding.UTF8.GetBytes("legit payload"),
        };
        string dir = LooseCliPackage.Create(_root, "pkgMissing", extra);

        // Remove a payload file that the block map expects.
        File.Delete(Path.Combine(dir, "Assets", "data.bin"));

        (int code, string output, _) = RunValidate(dir);

        Assert.NotEqual(0, code);
        Assert.Contains("INTEGRITY FAILED", output);
    }

    [Fact]
    public void Validate_DirectoryMutatedAfterOpen_ReportsDriftOnce()
    {
        string dir = LooseCliPackage.Create(_root, "pkgPostOpenMutation");
        using MsixPackage package = MsixPackage.OpenDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "evil.dll"), "attacker");

        ValidationReport report = ValidateCommand.Validate(package);

        Assert.False(report.IsValid);
        string driftError = Assert.Single(
            report.Errors,
            error => error.Contains("evil.dll", StringComparison.Ordinal));
        Assert.Contains("Package snapshot drift detected", driftError);
        Assert.Contains("evil.dll", driftError);
    }

    #endregion

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
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
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
    public void Pack_SourceDirectory_CreatesValidPackage()
    {
        string source = LooseCliPackage.Create(
            _root,
            "pack-source",
            new Dictionary<string, byte[]> { ["Data/value.txt"] = "payload"u8.ToArray() });
        string outputPath = Path.Combine(_root, "packed.msix");

        (int code, string output, string error) = RunPack(source, "-o", outputPath);

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("Identity:", output);
        Assert.Contains(Path.GetFullPath(outputPath), output);
        using MsixPackage package = MsixPackage.Open(outputPath);
        Assert.True(package.VerifyBlockMap().IsValid);
    }

    [Fact]
    public void Pack_Json_EmitsStructuredResult()
    {
        string source = LooseCliPackage.Create(_root, "pack-json");
        string outputPath = Path.Combine(_root, "packed-json.msix");

        (int code, string output, _) = RunPack(source, "--output", outputPath, "--json");

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Path.GetFullPath(outputPath), root.GetProperty("OutputPath").GetString());
        Assert.Equal("Contoso.MyApp", root.GetProperty("Name").GetString());
        Assert.Equal("1.2.3.4", root.GetProperty("Version").GetString());
        Assert.Equal("x64", root.GetProperty("Architecture").GetString());
        Assert.False(root.GetProperty("IsSigned").GetBoolean());
        Assert.Equal("Stored", root.GetProperty("Compression").GetString());
        Assert.True(root.GetProperty("FileCount").GetInt32() >= 1);
        Assert.True(root.GetProperty("TotalSize").GetInt64() > 0);
    }

    [Fact]
    public void Pack_CompressFlag_EmitsBlockDeflateAndReportsNormal()
    {
        string source = LooseCliPackage.Create(
            _root,
            "pack-compress",
            new Dictionary<string, byte[]>
            {
                ["Data/value.bin"] = Enumerable.Repeat((byte)'A', 70000).ToArray(),
            });
        string outputPath = Path.Combine(_root, "packed-compressed.msix");

        (int code, string output, string error) = RunPack(
            source,
            "--output",
            outputPath,
            "--compress",
            "--json");

        Assert.Equal(0, code);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal("Normal", document.RootElement.GetProperty("Compression").GetString());
        using MsixPackage package = MsixPackage.Open(outputPath);
        Assert.True(package.VerifyBlockMap().IsValid);
        Assert.All(
            package.BlockMap.Files.Single(static file => file.Name == "Data/value.bin").Blocks,
            static block => Assert.NotNull(block.CompressedSize));
    }

    [Fact]
    public void Pack_MissingManifest_ReturnsRuntimeError()
    {
        string source = Path.Combine(_root, "pack-no-manifest");
        Directory.CreateDirectory(source);
        string outputPath = Path.Combine(_root, "missing.msix");

        (int code, _, string error) = RunPack(source, "-o", outputPath);

        Assert.Equal(3, code);
        Assert.Contains("AppxManifest.xml", error);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData()]
    [InlineData("source")]
    [InlineData("-o", "output.msix")]
    [InlineData("source", "--bogus")]
    [InlineData("source", "-o")]
    public void Pack_BadArguments_ReturnUsageError(params string[] args)
    {
        (int code, _, string error) = RunPack(args);

        Assert.Equal(2, code);
        Assert.Contains("Usage: msixmgr pack", error);
    }

    [Fact]
    public void Pack_OverwriteFlagControlsReplacement()
    {
        string source = LooseCliPackage.Create(_root, "pack-overwrite");
        string outputPath = Path.Combine(_root, "overwrite.msix");

        Assert.Equal(0, RunPack(source, "-o", outputPath).Code);
        (int withoutOverwrite, _, string error) = RunPack(source, "-o", outputPath);
        (int withOverwrite, _, _) = RunPack(source, "-o", outputPath, "--overwrite");

        Assert.Equal(3, withoutOverwrite);
        Assert.Contains("already exists", error);
        Assert.Equal(0, withOverwrite);
    }

    [Fact]
    public void Program_MakeMsixAlias_CreatesPackage()
    {
        string source = LooseCliPackage.Create(_root, "makemsix-alias");
        string outputPath = Path.Combine(_root, "alias.msix");
        var output = new StringWriter();
        var error = new StringWriter();

        int code = Program.Run(["makemsix", source, "-o", outputPath], output, error);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void Bundle_PackagePaths_CreatesReadableBundle()
    {
        (string x64, string arm64) = CreateBundlePackages();
        string outputPath = Path.Combine(_root, "cli.msixbundle");

        (int code, string output, string error) = RunBundle(
            x64,
            arm64,
            "-o",
            outputPath,
            "--version",
            "5.4.3.2");

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("Bundled 2 packages", output);
        using MsixBundle bundle = MsixBundle.Open(outputPath);
        Assert.Equal(new Version(5, 4, 3, 2), bundle.Identity.Version);
        Assert.Equal(2, bundle.Packages.Count);
    }

    [Fact]
    public void Bundle_Json_EmitsTrimSafeStructuredReport()
    {
        (string x64, string arm64) = CreateBundlePackages();
        string outputPath = Path.Combine(_root, "cli-json.msixbundle");

        (int code, string output, string error) = RunBundle(
            x64,
            arm64,
            "--output",
            outputPath,
            "--json");

        Assert.Equal(0, code);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("PackageCount").GetInt32());
        Assert.Equal(2, root.GetProperty("Packages").GetArrayLength());
        Assert.Equal("application", root.GetProperty("Packages")[0].GetProperty("Type").GetString());
    }

    [Theory]
    [InlineData()]
    [InlineData("input.msix")]
    [InlineData("-o", "output.msixbundle")]
    [InlineData("input.msix", "-o")]
    [InlineData("input.msix", "-o", "output.msixbundle", "--version", "1.2.3")]
    [InlineData("input.msix", "-o", "output.msixbundle", "--bogus")]
    public void Bundle_BadArguments_ReturnUsageError(params string[] args)
    {
        (int code, _, string error) = RunBundle(args);

        Assert.Equal(2, code);
        Assert.Contains("Usage: msixmgr bundle", error);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("validate")]
    [InlineData("unpack")]
    [InlineData("pack")]
    [InlineData("bundle")]
    public void JsonUsageErrors_EmitErrorReportOnStdout(string verb)
    {
        (int code, string output, string error) = verb switch
        {
            "inspect" => RunInspect("--bogus", "--json"),
            "validate" => RunValidate("--bogus", "--json"),
            "unpack" => RunUnpack("--bogus", "--json"),
            "pack" => RunPack("--bogus", "--json"),
            "bundle" => RunBundle("--bogus", "--json"),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };

        Assert.Equal(2, code);
        Assert.Empty(error);
        AssertJsonError(output, "usage");
    }

    [Theory]
    [InlineData("pack-output")]
    [InlineData("unpack-destination")]
    [InlineData("bundle-output")]
    [InlineData("bundle-version")]
    public void JsonOptionValues_DoNotSwallowRecognizedOptions(string scenario)
    {
        (string x64, _) = CreateBundlePackages();

        (int code, string output, string error) = scenario switch
        {
            "pack-output" => RunPack("source", "-o", "--json"),
            "unpack-destination" => RunUnpack("input.msix", "-Destination", "--json"),
            "bundle-output" => RunBundle(x64, "-o", "--json"),
            "bundle-version" => RunBundle(x64, "-o", "out.msixbundle", "--version", "--json"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        Assert.Equal(2, code);
        Assert.Empty(error);
        AssertJsonError(output, "usage");
    }

    [Fact]
    public void Bundle_OutOfRangeVersion_IsJsonUsageError()
    {
        (string x64, _) = CreateBundlePackages();

        (int code, string output, string error) = RunBundle(
            x64,
            "-o",
            "out.msixbundle",
            "--version",
            "65536.0.0.0",
            "--json");

        Assert.Equal(2, code);
        Assert.Empty(error);
        AssertJsonError(output, "usage");
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("bundle")]
    public void PackAndBundle_NotSupportedException_IsOperationalJsonError(string verb)
    {
        Func<string, string, MsixCore.Packaging.Authoring.PackOptions?, MsixCore.Packaging.Authoring.PackResult> originalPack
            = PackCommand.BuildPackage;
        Func<IEnumerable<string>, string, MsixCore.Packaging.Authoring.BundleOptions?, MsixCore.Packaging.Authoring.BundleResult> originalBundle
            = BundleCommand.BuildBundle;
        try
        {
            PackCommand.BuildPackage = (_, _, _) => throw new NotSupportedException("zip64 is not supported");
            BundleCommand.BuildBundle = (_, _, _) => throw new NotSupportedException("zip64 is not supported");

            (int code, string output, string error) = verb switch
            {
                "pack" => RunPack("source", "-o", "out.msix", "--json"),
                "bundle" => RunBundle("input.msix", "-o", "out.msixbundle", "--json"),
                _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
            };

            Assert.Equal(3, code);
            Assert.Empty(error);
            AssertJsonError(output, "not_supported");
        }
        finally
        {
            PackCommand.BuildPackage = originalPack;
            BundleCommand.BuildBundle = originalBundle;
        }
    }

    [Theory]
    [InlineData("inspect", "not_found")]
    [InlineData("validate", "not_found")]
    [InlineData("unpack", "not_found")]
    [InlineData("pack", "footprint_missing")]
    [InlineData("bundle", "invalid_data")]
    public void JsonOperationalErrors_EmitErrorReportOnStdout(string verb, string expectedCode)
    {
        string missingPath = Path.Combine(_root, "missing.msix");
        string source = Path.Combine(_root, "pack-no-manifest-json");
        Directory.CreateDirectory(source);
        string child = Path.Combine(_root, "child.txt");
        File.WriteAllText(child, "not a package");

        (int code, string output, string error) = verb switch
        {
            "inspect" => RunInspect(missingPath, "--json"),
            "validate" => RunValidate(missingPath, "--json"),
            "unpack" => RunUnpack(missingPath, "-Destination", Path.Combine(_root, "unpack-json-error"), "--json"),
            "pack" => RunPack(source, "-o", Path.Combine(_root, "out.msix"), "--json"),
            "bundle" => RunBundle(child, "-o", Path.Combine(_root, "out.msixbundle"), "--json"),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };

        Assert.Equal(3, code);
        Assert.Empty(error);
        AssertJsonError(output, expectedCode);
    }

    [Theory]
    [InlineData("malformed-xml", "xml")]
    [InlineData("missing-manifest", "footprint_missing")]
    public void Inspect_Json_ReportsSpecificMsixErrorCode(string scenario, string expectedCode)
    {
        string directory = LooseCliPackage.Create(_root, "coded-" + scenario);
        string manifest = Path.Combine(directory, LooseCliPackage.ManifestName);
        if (scenario == "malformed-xml")
        {
            File.WriteAllText(manifest, "<Package>");
        }
        else
        {
            File.Delete(manifest);
        }

        (int code, string output, string error) = RunInspect(directory, "--json");

        Assert.Equal(3, code);
        Assert.Empty(error);
        AssertJsonError(output, expectedCode);
    }

    [Fact]
    public void ErrorCode_SnakeCaseRoundTripsEveryEnumMember()
    {
        foreach (MsixErrorCode code in Enum.GetValues<MsixErrorCode>())
        {
            string snakeCase = CliContract.ToSnakeCase(code);
            string roundTripped = string.Concat(
                snakeCase.Split('_').Select(static segment =>
                    char.ToUpperInvariant(segment[0]) + segment[1..]));

            Assert.Equal(code.ToString(), roundTripped);
            Assert.Equal(snakeCase, CliContract.ErrorCode(MsixError.Format(code, "test")));
            Assert.All(
                snakeCase,
                static character => Assert.True(char.IsAsciiLetterLower(character) || character == '_'));
        }
    }

    [Fact]
    public void ExitCodeMatrix_UsesContractCodes()
    {
        string valid = LooseCliPackage.Create(_root, "exit-valid");
        string invalid = LooseCliPackage.Create(_root, "exit-invalid");
        LooseCliPackage.CorruptBlockMap(invalid);
        string missing = Path.Combine(_root, "does-not-exist.msix");

        Assert.Equal(0, RunValidate(valid).Code);
        Assert.Equal(1, RunValidate(invalid).Code);
        Assert.Equal(2, RunValidate("--bogus").Code);
        Assert.Equal(3, RunValidate(missing).Code);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("validate")]
    [InlineData("unpack")]
    [InlineData("pack")]
    [InlineData("bundle")]
    public void HumanErrors_WritePlainTextToStderrAndUseContractCodes(string verb)
    {
        string missingPath = Path.Combine(_root, "missing.msix");
        string source = Path.Combine(_root, "pack-no-manifest-human");
        Directory.CreateDirectory(source);
        string child = Path.Combine(_root, "child-human.txt");
        File.WriteAllText(child, "not a package");

        (int usageCode, string usageOutput, string usageError) = verb switch
        {
            "inspect" => RunInspect("--bogus"),
            "validate" => RunValidate("--bogus"),
            "unpack" => RunUnpack("--bogus"),
            "pack" => RunPack("--bogus"),
            "bundle" => RunBundle("--bogus"),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };
        (int operationalCode, string operationalOutput, string operationalError) = verb switch
        {
            "inspect" => RunInspect(missingPath),
            "validate" => RunValidate(missingPath),
            "unpack" => RunUnpack(missingPath, "-Destination", Path.Combine(_root, "unpack-human-error")),
            "pack" => RunPack(source, "-o", Path.Combine(_root, "human.msix")),
            "bundle" => RunBundle(child, "-o", Path.Combine(_root, "human.msixbundle")),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };

        Assert.Equal(2, usageCode);
        Assert.Empty(usageOutput);
        Assert.Contains($"Usage: msixmgr {verb}", usageError);
        Assert.Equal(3, operationalCode);
        Assert.Empty(operationalOutput);
        Assert.Contains($"msixmgr {verb}:", operationalError);
        Assert.DoesNotContain("\"schemaVersion\"", usageError);
        Assert.DoesNotContain("\"schemaVersion\"", operationalError);
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
            .Split("For inspect,", StringSplitOptions.None)[0];
        string[] helpVerbs = verbSection
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimStart().Split(' ', 2)[0])
            .ToArray();
        string[] registeredVerbs = Program.Verbs.Select(static verb => verb.Name).ToArray();

        Assert.Equal(registeredVerbs, helpVerbs);
        CliVerb pack = Assert.Single(Program.Verbs, static verb => verb.Name == "pack");
        Assert.Contains("makemsix", pack.Aliases!);
        Assert.Contains("alias: makemsix", help);

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

    private static void AssertJsonError(string output, string expectedCode)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }

    private (string X64, string Arm64) CreateBundlePackages()
    {
        string x64Source = LooseCliPackage.Create(_root, "bundle-x64");
        string arm64Source = LooseCliPackage.Create(_root, "bundle-arm64");
        string arm64Manifest = Path.Combine(arm64Source, "AppxManifest.xml");
        File.WriteAllText(
            arm64Manifest,
            File.ReadAllText(arm64Manifest).Replace(
                "ProcessorArchitecture=\"x64\"",
                "ProcessorArchitecture=\"arm64\"",
                StringComparison.Ordinal),
            new UTF8Encoding(false));

        string x64 = Path.Combine(_root, "cli-x64.msix");
        string arm64 = Path.Combine(_root, "cli-arm64.msix");
        Assert.Equal(0, RunPack(x64Source, "-o", x64).Code);
        Assert.Equal(0, RunPack(arm64Source, "-o", arm64).Code);
        return (x64, arm64);
    }
}
