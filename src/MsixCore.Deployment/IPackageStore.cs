using MsixCore.Packaging;

namespace MsixCore.Deployment;

/// <summary>
/// Abstraction over where installed packages are recorded and their unpacked payloads live. The
/// default cross-platform implementation is a self-contained store root
/// (<see cref="FileSystemPackageStore"/>); a Windows OS-integrated store can be added later without
/// changing the query surface.
/// </summary>
public interface IPackageStore
{
    /// <summary>
    /// Enumerates the installed packages. Each returned package is owned by the caller and must be
    /// disposed.
    /// </summary>
    IReadOnlyList<IInstalledPackage> EnumeratePackages();
}
