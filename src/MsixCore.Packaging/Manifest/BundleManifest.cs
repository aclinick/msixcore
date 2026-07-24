namespace MsixCore.Packaging.Manifest;

/// <summary>The role of a package contained in a bundle.</summary>
public enum BundlePackageType
{
    /// <summary>An application package.</summary>
    Application = 0,

    /// <summary>A resource package (language, scale, or DXFL resources).</summary>
    Resource = 1,
}

/// <summary>A single package entry within an <c>AppxBundleManifest.xml</c>.</summary>
public sealed record BundlePackageEntry
{
    /// <summary>The package file name within the bundle container.</summary>
    public required string FileName { get; init; }

    /// <summary>Whether the entry is an application or resource package.</summary>
    public BundlePackageType Type { get; init; } = BundlePackageType.Application;

    /// <summary>The contained package version.</summary>
    public required Version Version { get; init; }

    /// <summary>The contained package architecture (applications only; resources are neutral).</summary>
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.Neutral;

    /// <summary>The resource id for resource packages; empty for application packages.</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>The resource qualifiers (e.g. languages/scales) the package provides.</summary>
    public IReadOnlyList<string> Resources { get; init; } = [];
}

/// <summary>The strongly-typed contents of an <c>AppxBundleManifest.xml</c>.</summary>
public sealed record BundleManifest
{
    /// <summary>The bundle identity (name, publisher, version). Bundles are architecture-neutral.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The packages contained in the bundle.</summary>
    public IReadOnlyList<BundlePackageEntry> Packages { get; init; } = [];
}
