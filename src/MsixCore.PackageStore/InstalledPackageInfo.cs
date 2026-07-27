using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.PackageStore;

/// <summary>Lightweight installed-package metadata that owns no file handles.</summary>
public sealed record InstalledPackageInfo
{
    /// <summary>The package identity.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The absolute installed package root.</summary>
    public required string InstalledLocation { get; init; }

    /// <summary>The user-facing display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The publisher display name.</summary>
    public required string PublisherDisplayName { get; init; }

    /// <summary>The declared capabilities.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// Whether this is a framework package (<c>Properties/Framework</c>).
    /// </summary>
    /// <remarks>
    /// Frameworks are installed side by side across versions — an app binds to a specific
    /// <c>MinVersion</c>, so installing a newer framework must not evict the older one that an
    /// already-installed app resolved against. The store uses this to decide what a commit replaces.
    /// </remarks>
    public bool IsFramework { get; init; }

    /// <summary>The package-relative logo path, if declared.</summary>
    public string? LogoPath { get; init; }

    /// <summary>The package-relative primary executable path, if declared.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Reads only the installed manifest and returns its metadata.</summary>
    public static InstalledPackageInfo ReadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string root = Path.GetFullPath(directory);
        string manifestPath = FindManifest(root)
            ?? throw MsixError.Format(MsixErrorCode.FootprintMissing, $"The installed package does not contain '{OpcPartNames.AppxManifest}'.");

        using Stream manifestStream = File.OpenRead(manifestPath);
        AppxManifest manifest = AppxManifestParser.Parse(manifestStream);
        return new InstalledPackageInfo
        {
            Identity = manifest.Identity,
            InstalledLocation = root,
            DisplayName = manifest.DisplayName,
            PublisherDisplayName = manifest.PublisherDisplayName,
            Capabilities = manifest.Capabilities,
            IsFramework = manifest.IsFramework,
            LogoPath = manifest.Logo,
            ExecutablePath = manifest.Applications
                .FirstOrDefault(static application => !string.IsNullOrEmpty(application.Executable))
                ?.Executable,
        };
    }

    /// <summary>Opens the installed package content on demand.</summary>
    public MsixPackage OpenPackage() => MsixPackage.OpenDirectory(InstalledLocation);

    /// <summary>
    /// Locates the manifest in an installed layout, or <see langword="null"/> when the directory
    /// does not contain one or cannot be read.
    /// </summary>
    /// <remarks>
    /// A <em>directory</em> or reparse point named <c>AppxManifest.xml</c> is rejected rather than
    /// reported as absent. It is not an accident, and answering "there is no manifest here" would
    /// hide the attempt behind an ordinary-looking error.
    /// </remarks>
    private static string? FindManifest(string directory)
    {
        string path;
        FileAttributes attributes;
        try
        {
            if (!File.GetAttributes(directory).HasFlag(FileAttributes.Directory))
            {
                return null;
            }

            path = Path.Combine(directory, OpcPartNames.AppxManifest);
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                // Case-sensitive filesystems: the part name is canonical, the on-disk name may not
                // match its casing.
                string? found = Directory.EnumerateFiles(directory)
                    .FirstOrDefault(file => string.Equals(
                        Path.GetFileName(file),
                        OpcPartNames.AppxManifest,
                        StringComparison.OrdinalIgnoreCase));
                if (found is null)
                {
                    return null;
                }

                path = found;
                attributes = File.GetAttributes(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // Deliberately outside the catch above: this rejection must reach the caller.
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            ? throw MsixError.Format(
                MsixErrorCode.PackageStore,
                $"The installed package manifest '{path}' is not a regular file.")
            : path;
    }
}
