using MsixCore.Studio.Services;
using MsixCore.Studio.ViewModels;

namespace MsixCore.Studio.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task LoadPackageFile_PopulatesManifestAndValidBlockMap()
    {
        using var fixture = new TestPackageFixture();
        string path = fixture.CreatePackageFile(signed: false, out _);
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(path, isDirectory: false);

        AssertLoadedManifest(viewModel);
        Assert.True(viewModel.IsBlockMapValid);
        Assert.Equal(2, viewModel.BlockMapFiles.Count);
        Assert.All(viewModel.BlockMapFiles, file => Assert.Equal("Valid", file.Verification));
        Assert.Null(viewModel.Signature);
        Assert.Equal("Unsigned package", viewModel.SignatureStatus);
    }

    [Fact]
    public async Task LoadLooseDirectory_PopulatesSameManifestData()
    {
        using var fixture = new TestPackageFixture();
        string path = fixture.CreateLooseDirectory(tampered: false);
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(path, isDirectory: true);

        AssertLoadedManifest(viewModel);
        Assert.True(viewModel.IsBlockMapValid);
        Assert.Equal(2, viewModel.BlockMapFiles.Count);
    }

    [Fact]
    public async Task LoadTamperedDirectory_SurfacesInvalidBlockMap()
    {
        using var fixture = new TestPackageFixture();
        string path = fixture.CreateLooseDirectory(tampered: true);
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(path, isDirectory: true);

        Assert.True(viewModel.HasPackage);
        Assert.False(viewModel.IsBlockMapValid);
        BlockMapFileItem payload = Assert.Single(
            viewModel.BlockMapFiles,
            file => file.Name == "payload.bin");
        Assert.Contains("hash mismatch", payload.Verification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadSignedPackage_MapsSignatureInformation()
    {
        using var fixture = new TestPackageFixture();
        string path = fixture.CreatePackageFile(signed: true, out SignatureExpectation? expected);
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(path, isDirectory: false);

        Assert.NotNull(expected);
        PackageSignatureItem signature = Assert.IsType<PackageSignatureItem>(viewModel.Signature);
        Assert.Equal(expected.SubjectName, signature.SubjectName);
        Assert.Equal(expected.IssuerName, signature.IssuerName);
        Assert.Equal(expected.Thumbprint, signature.Thumbprint);
        Assert.Equal(expected.NotBefore, signature.NotBefore);
        Assert.Equal(expected.NotAfter, signature.NotAfter);
        Assert.True(signature.IsCmsIntegrityValid);
        Assert.True(signature.MatchesPublisher);
        Assert.Equal("Signed", viewModel.SignatureStatus);
    }

    [Fact]
    public async Task LoadNonPackageFile_SurfacesCleanError()
    {
        using var fixture = new TestPackageFixture();
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(fixture.CreateNonPackageFile(), isDirectory: false);

        AssertError(viewModel);
    }

    [Fact]
    public async Task LoadMissingPath_SurfacesCleanError()
    {
        using var fixture = new TestPackageFixture();
        var viewModel = CreateViewModel();

        await viewModel.LoadPackageAsync(fixture.MissingPackagePath, isDirectory: false);

        AssertError(viewModel);
    }

    private static MainWindowViewModel CreateViewModel() =>
        new(new UnusedStoragePicker());

    private static void AssertLoadedManifest(MainWindowViewModel viewModel)
    {
        Assert.True(viewModel.HasPackage);
        Assert.False(viewModel.HasError);
        Assert.Equal("Contoso.StudioTest", viewModel.IdentityName);
        Assert.Equal("CN=Contoso", viewModel.Publisher);
        Assert.Equal("2.3.4.5", viewModel.Version);
        Assert.Equal("x64", viewModel.Architecture);
        Assert.Equal("Contoso.StudioTest_h91ms92gdsmmt", viewModel.PackageFamilyName);
        Assert.Equal("Contoso.StudioTest_2.3.4.5_x64__h91ms92gdsmmt", viewModel.PackageFullName);
        Assert.Equal("Studio Test", viewModel.DisplayName);
        Assert.Equal("Contoso Ltd", viewModel.PublisherDisplayName);
        Assert.Equal(["internetClient", "runFullTrust"], viewModel.Capabilities);

        ApplicationItem application = Assert.Single(viewModel.Applications);
        Assert.Equal("App", application.Id);
        Assert.Equal("Studio Test App", application.DisplayName);
        Assert.Equal("StudioTest.exe", application.Executable);
        Assert.Equal("Windows.FullTrustApplication", application.EntryPoint);
    }

    private static void AssertError(MainWindowViewModel viewModel)
    {
        Assert.False(viewModel.HasPackage);
        Assert.True(viewModel.HasError);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
        Assert.Equal("Unable to load package.", viewModel.StatusMessage);
    }

    private sealed class UnusedStoragePicker : IStoragePicker
    {
        public Task<string?> PickPackageAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
