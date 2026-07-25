using System.Text;

namespace MsixCore.Deployment.Tests;

/// <summary>Builds unpacked ("loose") package folders on disk for deployment-layer tests.</summary>
internal static class LoosePackageBuilder
{
    public static string ManifestXml(
        string name = "Contoso.MyApp",
        string publisher = "CN=Contoso",
        string version = "1.0.0.0",
        string architecture = "x64",
        string? executable = "App/App.exe") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="{name}" Publisher="{publisher}" Version="{version}" ProcessorArchitecture="{architecture}" />
          <Properties>
            <DisplayName>Contoso My App</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
            <Logo>Assets\StoreLogo.png</Logo>
          </Properties>
          <Applications>
            <Application Id="App"{(executable is null ? "" : $" Executable=\"{executable}\"")} EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="My App" Description="d" BackgroundColor="#000000"
                                  Square150x150Logo="Assets\Square150.png" Square44x44Logo="Assets\Square44.png" />
            </Application>
          </Applications>
        </Package>
        """;

    /// <summary>Creates a loose package folder under <paramref name="root"/> and returns its path.</summary>
    public static string Create(
        string root,
        string folderName,
        string? manifestXml = null,
        bool includeExecutable = true)
    {
        string dir = Path.Combine(root, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"), manifestXml ?? ManifestXml(), Encoding.UTF8);

        if (includeExecutable)
        {
            string appDir = Path.Combine(dir, "App");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "App.exe"), "MZ");
        }

        return dir;
    }
}
