using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Deployment;

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
            ?? throw new InvalidDataException($"The installed package does not contain '{OpcPartNames.AppxManifest}'.");

        using Stream manifestStream = File.OpenRead(manifestPath);
        AppxManifest manifest = AppxManifestParser.Parse(manifestStream);
        return new InstalledPackageInfo
        {
            Identity = manifest.Identity,
            InstalledLocation = root,
            DisplayName = manifest.DisplayName,
            PublisherDisplayName = manifest.PublisherDisplayName,
            Capabilities = manifest.Capabilities,
            LogoPath = manifest.Logo,
            ExecutablePath = manifest.Applications
                .FirstOrDefault(static application => !string.IsNullOrEmpty(application.Executable))
                ?.Executable,
        };
    }

    /// <summary>Opens the installed package content on demand.</summary>
    public MsixPackage OpenPackage() => MsixPackage.OpenDirectory(InstalledLocation);

    internal static string? FindManifest(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            string canonical = Path.Combine(directory, OpcPartNames.AppxManifest);
            if (IsRegularFile(canonical))
            {
                return canonical;
            }

            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(file =>
                    string.Equals(
                        Path.GetFileName(file),
                        OpcPartNames.AppxManifest,
                        StringComparison.OrdinalIgnoreCase)
                    && IsRegularFile(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsRegularFile(string path) =>
        File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
