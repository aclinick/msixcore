using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixCore.PackageStore.Tests;

/// <summary>
/// Covers resolution of the dependencies a package declares (TC-P1-3d) against a set of installed
/// packages: which are satisfied, which are missing, and which of the missing actually block.
/// </summary>
/// <remarks>
/// The installed packages are built as real loose folders and read back through
/// <see cref="InstalledPackageInfo.ReadFromDirectory"/> rather than constructed as records, so the
/// manifest fields resolution depends on — family name, architecture, version, the framework flag —
/// are the ones a real package would actually produce.
/// </remarks>
public class DependencyResolutionTests : IDisposable
{
    private const string Publisher = "CN=Contoso";
    private const string OtherPublisher = "CN=Fabrikam";

    private readonly string _root;
    private readonly List<InstalledPackageInfo> _installed = [];
    private int _next;

    public DependencyResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-deps-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Adds a package to the installed set the resolver will be given.</summary>
    private void GivenInstalled(
        string name,
        string version = "1.0.0.0",
        string architecture = "x64",
        string publisher = Publisher,
        bool isFramework = false)
    {
        string directory = LoosePackageBuilder.Create(
            _root,
            $"installed{_next++}",
            LoosePackageBuilder.ManifestXml(
                name,
                publisher,
                version,
                architecture,
                executable: null,
                isFramework: isFramework),
            includeExecutable: false);

        _installed.Add(InstalledPackageInfo.ReadFromDirectory(directory));
    }

    /// <summary>Builds the manifest of a package declaring <paramref name="dependencies"/>.</summary>
    private AppxManifest GivenPackageDeclaring(string? dependencies, string architecture = "x64")
    {
        string path = PackedMsixBuilder.Create(
            Path.Combine(_root, $"source{_next++}"),
            "app.msix",
            LoosePackageBuilder.ManifestXml(
                "Contoso.MyApp",
                Publisher,
                architecture: architecture,
                executable: null,
                dependencies: dependencies));

        using MsixPackage package = MsixPackage.Open(path);
        return package.Manifest;
    }

    private DependencyResolutionResult Resolve(AppxManifest manifest) =>
        DependencyResolver.Resolve(manifest, _installed);

    // TC-P1-3d
    [Fact]
    public void Resolve_WithAMissingFramework_ReportsNotInstalled()
    {
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolutionResult result = Resolve(manifest);

        DependencyResolution resolution = Assert.Single(result.Resolutions);
        Assert.Equal(DependencyResolutionStatus.NotInstalled, resolution.Status);
        Assert.False(result.CanDeploy);
        Assert.Contains("Contoso.Framework", resolution.Describe(), StringComparison.Ordinal);
        Assert.Contains("not installed", resolution.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WithAnInstalledFramework_IsSatisfied()
    {
        GivenInstalled("Contoso.Framework", "1.2.0.0", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolutionResult result = Resolve(manifest);

        Assert.True(result.IsSatisfied);
        Assert.True(result.CanDeploy);
        Assert.Empty(result.Unsatisfied);
        Assert.Equal(
            new Version(1, 2, 0, 0),
            Assert.Single(result.Resolutions).ResolvedPackage!.Identity.Version);
    }

    [Fact]
    public void Resolve_WithANonFrameworkOfTheSameName_IsNotSatisfied()
    {
        // A PackageDependency references a framework. An ordinary app package sharing the family
        // name is not loadable as one, so it must not satisfy the dependency.
        GivenInstalled("Contoso.Framework", "1.2.0.0", isFramework: false);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.NotAFramework, resolution.Status);
        Assert.Contains("is not a framework package", resolution.Describe(), StringComparison.Ordinal);

        // The near-miss is reported so a caller can explain what it found, not only what it wanted.
        Assert.Equal("Contoso.Framework", resolution.ResolvedPackage!.Identity.Name);
    }

    [Fact]
    public void Resolve_WithAFrameworkOlderThanMinVersion_IsNotSatisfied()
    {
        GivenInstalled("Contoso.Framework", "1.0.0.0", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="2.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.VersionTooLow, resolution.Status);
        Assert.Contains("2.0.0.0 or later", resolution.Describe(), StringComparison.Ordinal);
        Assert.Equal(new Version(1, 0, 0, 0), resolution.ResolvedPackage!.Identity.Version);
    }

    [Fact]
    public void Resolve_WithAFrameworkNewerThanMinVersion_IsSatisfied()
    {
        // MinVersion is a floor, not a pin.
        GivenInstalled("Contoso.Framework", "9.0.0.0", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="2.0.0.0" Publisher="{Publisher}" />""");

        Assert.True(Resolve(manifest).IsSatisfied);
    }

    [Fact]
    public void Resolve_WithNoDependencies_RequiresNothing()
    {
        AppxManifest manifest = GivenPackageDeclaring(dependencies: null);

        DependencyResolutionResult result = Resolve(manifest);

        Assert.Empty(result.Resolutions);
        Assert.True(result.IsSatisfied);
        Assert.True(result.CanDeploy);
    }

    [Fact]
    public void Resolve_WithNoDependencies_DoesNotEnumerateTheInstalledSet()
    {
        // A caller whose inventory is expensive to produce should not pay for it when the package
        // declares nothing. The sequence throws on enumeration, not on construction, so it fails only
        // if Resolve actually touches it.
        AppxManifest manifest = GivenPackageDeclaring(dependencies: null);
        IEnumerable<InstalledPackageInfo> throwOnEnumeration = Enumerable.Range(0, 1)
            .Select<int, InstalledPackageInfo>(
                static _ => throw new InvalidOperationException("The installed set must not be enumerated."));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, throwOnEnumeration);

        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void Resolve_EnumeratesTheInstalledSetOnlyOnce()
    {
        // Resolve accepts IEnumerable, so a caller may pass a lazy or single-pass sequence; it must
        // be materialised rather than re-enumerated once per declared dependency.
        GivenInstalled("Contoso.One", isFramework: true);
        GivenInstalled("Contoso.Two", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.One" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Contoso.Two" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """);

        int enumerations = 0;
        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, Counted());

        Assert.True(result.IsSatisfied);
        Assert.Equal(1, enumerations);

        IEnumerable<InstalledPackageInfo> Counted()
        {
            enumerations++;
            foreach (InstalledPackageInfo package in _installed)
            {
                yield return package;
            }
        }
    }

    [Fact]
    public void Resolve_WithAMissingMainPackage_IsNotSatisfied()
    {
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.NotInstalled, resolution.Status);
        Assert.Contains(
            "main package 'Contoso.MainApp' is not installed",
            resolution.Describe(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WithAMissingHostRuntime_IsNotSatisfied()
    {
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<uap10:HostRuntimeDependency Name="Contoso.Host" Publisher="{Publisher}" MinVersion="1.0.0.0" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.NotInstalled, resolution.Status);
        Assert.Contains(
            "host runtime 'Contoso.Host' is not installed",
            resolution.Describe(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_MainPackageAndHostRuntime_NeedNotBeFrameworks()
    {
        // Only PackageDependency carries the framework constraint: the package a modification
        // package modifies, and a host runtime, are both ordinary packages.
        GivenInstalled("Contoso.MainApp", isFramework: false);
        GivenInstalled("Contoso.Host", isFramework: false);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""
            <uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />
                <uap10:HostRuntimeDependency Name="Contoso.Host" Publisher="{Publisher}" MinVersion="1.0.0.0" />
            """);

        Assert.True(Resolve(manifest).IsSatisfied);
    }

    [Fact]
    public void Resolve_MainPackageWithoutPublisher_UsesTheDependentsOwnPublisher()
    {
        // The uap3 form has no Publisher attribute; a modification package always shares its
        // parent's publisher, so resolution must fall back to the dependent's.
        GivenInstalled("Contoso.MainApp");
        AppxManifest manifest = GivenPackageDeclaring(
            """<uap4:MainPackageDependency Name="Contoso.MainApp" />""");

        Assert.True(Resolve(manifest).IsSatisfied);
    }

    [Fact]
    public void Resolve_MainPackageWithoutPublisher_DoesNotMatchAnotherPublisher()
    {
        // The fallback must narrow resolution to the dependent's own publisher, not widen it to any.
        GivenInstalled("Contoso.MainApp", publisher: OtherPublisher);
        AppxManifest manifest = GivenPackageDeclaring(
            """<uap4:MainPackageDependency Name="Contoso.MainApp" />""");

        Assert.Equal(
            DependencyResolutionStatus.NotInstalled,
            Assert.Single(Resolve(manifest).Resolutions).Status);
    }

    [Fact]
    public void Resolve_DependencyFromADifferentPublisher_IsNotSatisfied()
    {
        // Same package name, different publisher, therefore a different family: it must not match.
        GivenInstalled("Contoso.Framework", publisher: OtherPublisher, isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Assert.Equal(
            DependencyResolutionStatus.NotInstalled,
            Assert.Single(Resolve(manifest).Resolutions).Status);
    }

    [Fact]
    public void Resolve_FrameworkForADifferentArchitecture_IsNotSatisfied()
    {
        // An x86 framework cannot load into an x64 app's process.
        GivenInstalled("Contoso.Framework", architecture: "x86", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Assert.Equal(
            DependencyResolutionStatus.NotInstalled,
            Assert.Single(Resolve(manifest).Resolutions).Status);
    }

    [Fact]
    public void Resolve_PrefersTheMatchingArchitectureOverAHigherVersion()
    {
        // Architecture filtering happens before the version comparison, so a newer but unloadable
        // build must not shadow the older loadable one.
        GivenInstalled("Contoso.Framework", "9.0.0.0", architecture: "x86", isFramework: true);
        GivenInstalled("Contoso.Framework", "2.0.0.0", architecture: "x64", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(new Version(2, 0, 0, 0), resolution.ResolvedPackage!.Identity.Version);
    }

    [Fact]
    public void Resolve_NeutralFramework_SatisfiesAnyArchitecture()
    {
        GivenInstalled("Contoso.Framework", architecture: "neutral", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Assert.True(Resolve(manifest).IsSatisfied);
    }

    [Fact]
    public void Resolve_NeutralDependent_IsSatisfiedByAnyArchitecture()
    {
        GivenInstalled("Contoso.Framework", architecture: "x64", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""",
            architecture: "neutral");

        Assert.True(Resolve(manifest).IsSatisfied);
    }

    [Fact]
    public void Resolve_PrefersTheHighestInstalledVersion()
    {
        // Several versions of a framework family are legitimately installed side by side; the newest
        // compatible one decides whether the MinVersion constraint is met.
        GivenInstalled("Contoso.Framework", "1.0.0.0", isFramework: true);
        GivenInstalled("Contoso.Framework", "3.0.0.0", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="2.0.0.0" Publisher="{Publisher}" />""");

        DependencyResolution resolution = Assert.Single(Resolve(manifest).Resolutions);

        Assert.Equal(DependencyResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(new Version(3, 0, 0, 0), resolution.ResolvedPackage!.Identity.Version);
    }

    [Fact]
    public void Resolve_MissingOptionalDependency_IsReportedButNotBlocking()
    {
        AppxManifest manifest = GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.Optional" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="true" />
                <PackageDependency Name="Contoso.Required" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """);

        DependencyResolutionResult result = Resolve(manifest);

        // Both are unsatisfied and both are reported, but only the required one blocks.
        Assert.False(result.IsSatisfied);
        Assert.Equal(["Contoso.Optional", "Contoso.Required"], result.Unsatisfied.Select(r => r.Dependency.Name));
        Assert.False(result.CanDeploy);
        Assert.Equal(["Contoso.Required"], result.Blocking.Select(r => r.Dependency.Name));
    }

    [Fact]
    public void Resolve_WithOnlyOptionalDependenciesMissing_CanDeploy()
    {
        // uap6:Optional marks a framework the package can run without, so its absence must not block
        // deployment even though it is still reported as unsatisfied.
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Optional" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="true" />""");

        DependencyResolutionResult result = Resolve(manifest);

        Assert.False(result.IsSatisfied);
        Assert.True(result.CanDeploy);
        Assert.Empty(result.Blocking);
    }

    [Fact]
    public void Resolve_ReportsEveryUnsatisfiedDependency()
    {
        // A caller should be able to report everything that is missing at once, not just the first.
        AppxManifest manifest = GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.One" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Contoso.Two" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """);

        DependencyResolutionResult result = Resolve(manifest);

        Assert.False(result.IsSatisfied);
        Assert.Equal(["Contoso.One", "Contoso.Two"], result.Unsatisfied.Select(r => r.Dependency.Name));
    }

    [Fact]
    public void Resolve_PreservesManifestOrder()
    {
        GivenInstalled("Contoso.Two", isFramework: true);
        AppxManifest manifest = GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.One" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Contoso.Two" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Contoso.Three" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """);

        DependencyResolutionResult result = Resolve(manifest);

        Assert.Equal(
            ["Contoso.One", "Contoso.Two", "Contoso.Three"],
            result.Resolutions.Select(r => r.Dependency.Name));
        Assert.Equal([false, true, false], result.Resolutions.Select(r => r.IsSatisfied));
    }

    [Fact]
    public void Resolve_WithNullArguments_Throws()
    {
        AppxManifest manifest = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Assert.Throws<ArgumentNullException>(() => DependencyResolver.Resolve(null!, _installed));
        Assert.Throws<ArgumentNullException>(() => DependencyResolver.Resolve(manifest, null!));
    }
}
