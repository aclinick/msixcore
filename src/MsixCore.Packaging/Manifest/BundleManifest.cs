namespace MsixCore.Packaging.Manifest;

/// <summary>The role of a package contained in a bundle.</summary>
public enum BundlePackageType
{
    /// <summary>An application package.</summary>
    Application = 0,

    /// <summary>A resource package (language, scale, or DXFL resources).</summary>
    Resource = 1,
}

/// <summary>
/// A single <c>Resource</c> applicability qualifier declared on a bundle package. A resource may
/// carry any combination of <see cref="Language"/>, <see cref="Scale"/>, and
/// <see cref="DXFeatureLevel"/>.
/// </summary>
public sealed record BundleResource
{
    /// <summary>The BCP-47 language tag (e.g. <c>en-US</c>), if declared.</summary>
    public string? Language { get; init; }

    /// <summary>The scale qualifier (e.g. <c>200</c>), if declared.</summary>
    public string? Scale { get; init; }

    /// <summary>The DirectX feature level qualifier (e.g. <c>DX11</c>), if declared.</summary>
    public string? DXFeatureLevel { get; init; }
}

/// <summary>A single package entry within an <c>AppxBundleManifest.xml</c>.</summary>
public sealed record BundlePackageEntry
{
    /// <summary>The package file name within the bundle container.</summary>
    public required string FileName { get; init; }

    /// <summary>Whether the entry is an application or resource package.</summary>
    public BundlePackageType Type { get; init; } = BundlePackageType.Resource;

    /// <summary>The contained package version.</summary>
    public required Version Version { get; init; }

    /// <summary>The contained package architecture (applications only; resources are neutral).</summary>
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.Neutral;

    /// <summary>The resource id for resource packages; empty for application packages.</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>The resource applicability qualifiers (languages/scales/DXFL) the package provides.</summary>
    public IReadOnlyList<BundleResource> Resources { get; init; } = [];

    /// <summary>The absolute byte offset of the child package payload in the bundle ZIP.</summary>
    public long Offset { get; init; }

    /// <summary>The child package size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The target device families copied from the child package manifest.</summary>
    public IReadOnlyList<TargetDeviceFamily> TargetDeviceFamilies { get; init; } = [];
}

/// <summary>The strongly-typed contents of an <c>AppxBundleManifest.xml</c>.</summary>
public sealed record BundleManifest
{
    /// <summary>The bundle identity (name, publisher, version). Bundles are architecture-neutral.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The packages contained in the bundle.</summary>
    public IReadOnlyList<BundlePackageEntry> Packages { get; init; } = [];
}
