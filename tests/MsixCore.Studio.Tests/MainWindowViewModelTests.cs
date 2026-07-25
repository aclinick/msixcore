using System.Security.Cryptography;
using System.Text;
using MsixCore.Studio.Services;
using MsixCore.Studio.ViewModels;

namespace MsixCore.Studio.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _packageDirectory =
        Path.Combine(AppContext.BaseDirectory, $"studio-test-package-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadPackageAsync_ExposesLoosePackageDetails()
    {
        byte[] manifest = Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
              <Identity Name="Contoso.StudioTest" Publisher="CN=Contoso" Version="2.3.4.5" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>Studio Test</DisplayName>
                <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
              </Properties>
              <Applications>
                <Application Id="App" Executable="StudioTest.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="Studio Test App" Description="Test" />
                </Application>
              </Applications>
              <Capabilities>
                <Capability Name="internetClient" />
              </Capabilities>
            </Package>
            """);
        byte[] payload = [1, 2, 3, 4];

        Directory.CreateDirectory(_packageDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_packageDirectory, "AppxManifest.xml"), manifest);
        await File.WriteAllBytesAsync(Path.Combine(_packageDirectory, "payload.bin"), payload);
        await File.WriteAllTextAsync(
            Path.Combine(_packageDirectory, "AppxBlockMap.xml"),
            BuildBlockMap(("AppxManifest.xml", manifest), ("payload.bin", payload)));

        var viewModel = new MainWindowViewModel(new UnusedStoragePicker());
        await viewModel.LoadPackageAsync(_packageDirectory, isDirectory: true);

        Assert.True(viewModel.HasPackage);
        Assert.False(viewModel.HasError);
        Assert.Equal("Contoso.StudioTest", viewModel.IdentityName);
        Assert.Equal("Contoso.StudioTest_2.3.4.5_x64__h91ms92gdsmmt", viewModel.PackageFullName);
        Assert.Equal("internetClient", Assert.Single(viewModel.Capabilities));
        Assert.Equal("App", Assert.Single(viewModel.Applications).Id);
        Assert.Equal(2, viewModel.BlockMapFiles.Count);
        Assert.StartsWith("Valid", viewModel.BlockMapStatus, StringComparison.Ordinal);
        Assert.Equal("Unsigned package", viewModel.SignatureStatus);
    }

    [Fact]
    public async Task LoadPackageAsync_MalformedFolderReportsError()
    {
        Directory.CreateDirectory(_packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(_packageDirectory, "AppxManifest.xml"), "<not-package />");

        var viewModel = new MainWindowViewModel(new UnusedStoragePicker());
        await viewModel.LoadPackageAsync(_packageDirectory, isDirectory: true);

        Assert.False(viewModel.HasPackage);
        Assert.True(viewModel.HasError);
        Assert.Contains("could not be opened", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private static string BuildBlockMap(params (string Name, byte[] Content)[] files)
    {
        var builder = new StringBuilder(
            """<BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">""");

        foreach ((string name, byte[] content) in files)
        {
            string hash = Convert.ToBase64String(SHA256.HashData(content));
            builder.Append("<File Name=\"")
                .Append(name.Replace('/', '\\'))
                .Append("\" Size=\"")
                .Append(content.Length)
                .Append("\" LfhSize=\"0\"><Block Hash=\"")
                .Append(hash)
                .Append("\" /></File>");
        }

        return builder.Append("</BlockMap>").ToString();
    }

    private sealed class UnusedStoragePicker : IStoragePicker
    {
        public Task<string?> PickPackageAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
