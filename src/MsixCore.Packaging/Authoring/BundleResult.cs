using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Authoring;

/// <summary>Describes a successfully authored MSIX bundle.</summary>
public sealed record BundleResult
{
    /// <summary>The absolute path of the bundle that was written.</summary>
    public required string OutputPath { get; init; }

    /// <summary>The bundle identity read back from the generated bundle manifest.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The contained package entries.</summary>
    public required IReadOnlyList<BundlePackageEntry> Packages { get; init; }

    /// <summary>The number of contained packages.</summary>
    public int PackageCount => Packages.Count;

    /// <summary>The total size of the contained packages in bytes.</summary>
    public long TotalSize => Packages.Sum(static package => package.Size);
}
