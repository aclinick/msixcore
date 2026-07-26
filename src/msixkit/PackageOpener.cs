using MsixCore.Packaging;

namespace MsixKit;

/// <summary>
/// Opens a package from either a container file (<c>.msix</c>/<c>.appx</c>) or an unpacked
/// ("loose") directory, so every verb transparently supports both layouts.
/// </summary>
internal static class PackageOpener
{
    public static MsixPackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (Directory.Exists(path))
        {
            return MsixPackage.OpenDirectory(path);
        }

        if (File.Exists(path))
        {
            return MsixPackage.Open(path);
        }

        throw new FileNotFoundException($"No package file or directory found at '{path}'.", path);
    }
}
