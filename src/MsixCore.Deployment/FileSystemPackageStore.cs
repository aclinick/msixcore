using MsixCore.Packaging;
using MsixCore.Packaging.Opc;

namespace MsixCore.Deployment;

/// <summary>
/// A self-contained, cross-platform <see cref="IPackageStore"/> that keeps each installed package as
/// an unpacked subdirectory of a store root. A subdirectory is treated as an installed package when
/// it contains an <c>AppxManifest.xml</c>.
/// </summary>
public sealed class FileSystemPackageStore : IPackageStore
{
    /// <summary>The default store-root directory name placed under the user's local application data.</summary>
    public const string DefaultStoreFolderName = "MsixCore/Packages";

    private readonly string _root;

    /// <summary>Creates a store rooted at the given directory (created on demand).</summary>
    /// <param name="rootDirectory">The store-root directory that holds unpacked package folders.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public FileSystemPackageStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
    }

    /// <summary>The absolute store-root directory.</summary>
    public string RootDirectory => _root;

    /// <summary>Creates a store at the default per-user location.</summary>
    /// <returns>A <see cref="FileSystemPackageStore"/> under the local application data folder.</returns>
    public static FileSystemPackageStore CreateDefault()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new FileSystemPackageStore(Path.Combine(appData, "MsixCore", "Packages"));
    }

    /// <inheritdoc/>
    public IReadOnlyList<IInstalledPackage> EnumeratePackages()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var packages = new List<IInstalledPackage>();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_root))
            {
                if (!File.Exists(Path.Combine(directory, OpcPartNames.AppxManifest)))
                {
                    continue;
                }

                packages.Add(InstalledPackage.OpenDirectory(directory));
            }
        }
        catch
        {
            // Dispose anything opened before the failure to avoid leaks, then rethrow.
            foreach (IInstalledPackage package in packages)
            {
                package.Dispose();
            }

            throw;
        }

        return packages;
    }
}
