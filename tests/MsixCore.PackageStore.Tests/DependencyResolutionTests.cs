using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixCore.PackageStore.Tests;

/// <summary>
/// Covers install-time resolution of the dependencies a package declares (TC-P1-3d): a package
/// whose framework, main package, or host runtime is absent must not install.
/// </summary>
public class DependencyResolutionTests : IDisposable
{
    private const string Publisher = "CN=Contoso";
    private const string OtherPublisher = "CN=Fabrikam";

    private readonly string _root;
    private readonly FileSystemPackageStore _store;

    public DependencyResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "msixcore-deps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new FileSystemPackageStore(Path.Combine(_root, "store"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Places a package directly into the store, as though it were already installed.</summary>
    private void GivenInstalled(
        string name,
        string version = "1.0.0.0",
        string architecture = "x64",
        string publisher = Publisher,
        bool isFramework = false)
    {
        string staging = _store.CreateStagingLocation();
        File.WriteAllText(
            Path.Combine(staging, "AppxManifest.xml"),
            LoosePackageBuilder.ManifestXml(
                name,
                publisher,
                version,
                architecture,
                executable: null,
                isFramework: isFramework));
        _store.Commit(staging, InstalledPackageInfo.ReadFromDirectory(staging), DeploymentOptions.None);
    }

    private string GivenPackageDeclaring(string dependencies, string architecture = "x64")
    {
        string source = Path.Combine(_root, "source");
        return PackedMsixBuilder.Create(
            source,
            "app.msix",
            LoosePackageBuilder.ManifestXml(
                "Contoso.MyApp",
                Publisher,
                architecture: architecture,
                executable: null,
                dependencies: dependencies));
    }

    private static AppxManifest ManifestOf(string packagePath)
    {
        using MsixPackage package = MsixPackage.Open(packagePath);
        return package.Manifest;
    }

    private async Task<Exception?> InstallAsync(string packagePath, DeploymentOptions options = DeploymentOptions.None)
    {
        var manager = new PackageManager(_store);
        try
        {
            await manager.AddPackage(packagePath, options).Completion;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // TC-P1-3d
    [Fact]
    public async Task AddPackage_WithAMissingFrameworkDependency_Fails()
    {
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Exception? error = await InstallAsync(path);

        Assert.NotNull(error);
        Assert.Equal(MsixErrorCode.DependencyNotSatisfied, MsixError.GetCode(error));
        Assert.Contains("Contoso.Framework", error!.Message, StringComparison.Ordinal);
        Assert.Contains("not installed", error.Message, StringComparison.Ordinal);
        Assert.Empty(_store.EnumeratePackages());
    }

    [Fact]
    public async Task AddPackage_WithAnInstalledFrameworkDependency_Succeeds()
    {
        GivenInstalled("Contoso.Framework", "1.2.0.0");
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Exception? error = await InstallAsync(path);

        Assert.Null(error);
        Assert.Contains(_store.EnumeratePackages(), p => p.Identity.Name == "Contoso.MyApp");
    }

    [Fact]
    public async Task AddPackage_WithAFrameworkOlderThanMinVersion_Fails()
    {
        GivenInstalled("Contoso.Framework", "1.0.0.0");
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="2.0.0.0" Publisher="{Publisher}" />""");

        Exception? error = await InstallAsync(path);

        Assert.NotNull(error);
        Assert.Equal(MsixErrorCode.DependencyNotSatisfied, MsixError.GetCode(error));
        Assert.Contains("2.0.0.0 or later", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddPackage_WithASkippedDependencyCheck_InstallsAnyway()
    {
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Exception? error = await InstallAsync(path, DeploymentOptions.SkipDependencyCheck);

        Assert.Null(error);
        Assert.Contains(_store.EnumeratePackages(), p => p.Identity.Name == "Contoso.MyApp");
    }

    [Fact]
    public async Task AddPackage_WithNoDependencies_DoesNotRequireAnything()
    {
        string path = PackedMsixBuilder.Create(Path.Combine(_root, "source"), "plain.msix");

        Assert.Null(await InstallAsync(path));
    }

    [Fact]
    public async Task AddPackage_WithAMissingMainPackage_Fails()
    {
        string path = GivenPackageDeclaring(
            $"""<uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />""");

        Exception? error = await InstallAsync(path);

        Assert.NotNull(error);
        Assert.Contains("main package 'Contoso.MainApp' is not installed", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddPackage_WithAMissingHostRuntime_Fails()
    {
        string path = GivenPackageDeclaring(
            $"""<uap10:HostRuntimeDependency Name="Contoso.Host" Publisher="{Publisher}" MinVersion="1.0.0.0" />""");

        Exception? error = await InstallAsync(path);

        Assert.NotNull(error);
        Assert.Contains("host runtime 'Contoso.Host' is not installed", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_MainPackageWithoutPublisher_UsesTheDependentsOwnPublisher()
    {
        // The uap3 form has no Publisher attribute; a modification package always shares its
        // parent's publisher, so resolution must fall back to the dependent's.
        GivenInstalled("Contoso.MainApp");
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            """<uap4:MainPackageDependency Name="Contoso.MainApp" />"""));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void Resolve_DependencyFromADifferentPublisher_IsNotSatisfied()
    {
        // Same package name, different publisher, therefore a different family: it must not match.
        GivenInstalled("Contoso.Framework", publisher: OtherPublisher);
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />"""));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        DependencyResolution resolution = Assert.Single(result.Resolutions);
        Assert.Equal(DependencyResolutionStatus.NotInstalled, resolution.Status);
    }

    [Fact]
    public void Resolve_FrameworkForADifferentArchitecture_IsNotSatisfied()
    {
        // An x86 framework cannot load into an x64 app's process.
        GivenInstalled("Contoso.Framework", architecture: "x86");
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />"""));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        DependencyResolution resolution = Assert.Single(result.Resolutions);
        Assert.Equal(DependencyResolutionStatus.NotInstalled, resolution.Status);
    }

    [Fact]
    public void Resolve_NeutralFramework_SatisfiesAnyArchitecture()
    {
        GivenInstalled("Contoso.Framework", architecture: "neutral");
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />"""));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        Assert.True(result.IsSatisfied);
    }

    [Fact]
    public void Resolve_PrefersTheHighestInstalledVersion()
    {
        // A store legitimately holds several versions of a framework family; the newest one decides
        // whether the MinVersion constraint is met. Both must really still be present — if the store
        // evicted the older one on commit this test would pass for the wrong reason.
        GivenInstalled("Contoso.Framework", "1.0.0.0", isFramework: true);
        GivenInstalled("Contoso.Framework", "3.0.0.0", isFramework: true);
        Assert.Equal(2, _store.EnumeratePackages().Count(p => p.Identity.Name == "Contoso.Framework"));

        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="2.0.0.0" Publisher="{Publisher}" />"""));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        DependencyResolution resolution = Assert.Single(result.Resolutions);
        Assert.Equal(DependencyResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(new Version(3, 0, 0, 0), resolution.ResolvedPackage!.Identity.Version);
    }

    [Fact]
    public void Store_KeepsFrameworkVersionsSideBySide()
    {
        // An app binds to the specific MinVersion it declared, so installing a newer framework must
        // not evict the older one an already-installed app resolved against.
        GivenInstalled("Contoso.Framework", "1.0.0.0", isFramework: true);
        GivenInstalled("Contoso.Framework", "2.0.0.0", isFramework: true);

        Assert.Equal(
            [new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)],
            _store.EnumeratePackages().Select(p => p.Identity.Version).Order());
    }

    [Fact]
    public void Store_KeepsArchitectureVariantsSideBySide()
    {
        // An x86 app and an x64 app on one machine each need their own build of the framework.
        GivenInstalled("Contoso.Framework", architecture: "x64", isFramework: true);
        GivenInstalled("Contoso.Framework", architecture: "x86", isFramework: true);

        Assert.Equal(
            [ProcessorArchitecture.X86, ProcessorArchitecture.X64],
            _store.EnumeratePackages().Select(p => p.Identity.Architecture).Order());
    }

    [Fact]
    public void Store_ReplacesAnOlderVersionOfANonFrameworkPackage()
    {
        // App packages keep the single-slot-per-family upgrade behaviour.
        GivenInstalled("Contoso.App", "1.0.0.0");
        GivenInstalled("Contoso.App", "2.0.0.0");

        InstalledPackageInfo installed = Assert.Single(_store.EnumeratePackages());
        Assert.Equal(new Version(2, 0, 0, 0), installed.Identity.Version);
    }

    [Fact]
    public async Task AddPackage_WithAMissingOptionalDependency_StillInstalls()
    {
        // uap6:Optional marks a framework the package can run without, so its absence must not block
        // deployment.
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="true" />""");

        Exception? error = await InstallAsync(path);

        Assert.Null(error);
        Assert.Contains(_store.EnumeratePackages(), p => p.Identity.Name == "Contoso.MyApp");
    }

    [Fact]
    public void Resolve_MissingOptionalDependency_IsReportedButNotBlocking()
    {
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.Optional" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="true" />
                <PackageDependency Name="Contoso.Required" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        // Both are unsatisfied and both are reported, but only the required one blocks.
        Assert.False(result.IsSatisfied);
        Assert.Equal(["Contoso.Optional", "Contoso.Required"], result.Unsatisfied.Select(r => r.Dependency.Name));
        Assert.False(result.CanDeploy);
        Assert.Equal(["Contoso.Required"], result.Blocking.Select(r => r.Dependency.Name));
    }

    [Fact]
    public async Task AddPackage_WithOnlyOptionalDependenciesMissing_ReportsCanDeploy()
    {
        string path = GivenPackageDeclaring(
            $"""<PackageDependency Name="Contoso.Optional" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="true" />""");

        DependencyResolutionResult result = DependencyResolver.Resolve(ManifestOf(path), _store);

        Assert.False(result.IsSatisfied);
        Assert.True(result.CanDeploy);
        Assert.Null(await InstallAsync(path));
    }

    [Fact]
    public void Resolve_ReportsEveryUnsatisfiedDependency()
    {
        AppxManifest manifest = ManifestOf(GivenPackageDeclaring(
            $"""
            <PackageDependency Name="Contoso.One" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Contoso.Two" MinVersion="1.0.0.0" Publisher="{Publisher}" />
            """));

        DependencyResolutionResult result = DependencyResolver.Resolve(manifest, _store);

        Assert.False(result.IsSatisfied);
        Assert.Equal(["Contoso.One", "Contoso.Two"], result.Unsatisfied.Select(r => r.Dependency.Name));
    }
}
