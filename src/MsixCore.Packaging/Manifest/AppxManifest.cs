namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The strongly-typed contents of an <c>AppxManifest.xml</c>.
/// </summary>
public sealed record AppxManifest
{
    /// <summary>The package identity.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The user-facing package display name (<c>Properties/DisplayName</c>).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The publisher display name (<c>Properties/PublisherDisplayName</c>).</summary>
    public string PublisherDisplayName { get; init; } = string.Empty;

    /// <summary>The package description (<c>Properties/Description</c>), if declared.</summary>
    public string? Description { get; init; }

    /// <summary>The package-relative path to the package logo (<c>Properties/Logo</c>), if declared.</summary>
    public string? Logo { get; init; }

    /// <summary>Whether the package is a framework package (<c>Properties/Framework</c> is <c>true</c>).</summary>
    public bool IsFramework { get; init; }

    /// <summary>Whether this is a resource-only package (<c>Properties/ResourcePackage</c> is <c>true</c>).</summary>
    public bool IsResourcePackage { get; init; }

    /// <summary>The package's declared language, scale, and DirectX resource qualifiers.</summary>
    public IReadOnlyList<BundleResource> Resources { get; init; } = [];

    /// <summary>The declared capabilities (e.g. <c>runFullTrust</c>, <c>internetClient</c>).</summary>
    /// <remarks>
    /// Names only, de-duplicated and in document order. Use <see cref="DeclaredCapabilities"/> when
    /// the category (general, device, restricted, custom) matters.
    /// </remarks>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// The declared capabilities with their category and declaring namespace, in document order.
    /// </summary>
    public IReadOnlyList<ManifestCapability> DeclaredCapabilities { get; init; } = [];

    /// <summary>The declared applications.</summary>
    public IReadOnlyList<ManifestApplication> Applications { get; init; } = [];

    /// <summary>The declared <c>TargetDeviceFamily</c> dependencies.</summary>
    public IReadOnlyList<TargetDeviceFamily> TargetDeviceFamilies { get; init; } = [];

    /// <summary>
    /// The declared package-to-package dependencies: framework packages, the modified main package,
    /// and the host runtime.
    /// </summary>
    public IReadOnlyList<PackageDependency> PackageDependencies { get; init; } = [];

    /// <summary>
    /// The extensions declared by the package itself, i.e. <c>&lt;Package&gt;&lt;Extensions&gt;</c>.
    /// Extensions declared by an application are on
    /// <see cref="ManifestApplication.Extensions"/> instead; the two containers are separate in the
    /// schema and are kept separate here because a package-level extension has no owning app.
    /// </summary>
    public IReadOnlyList<AppExtension> Extensions { get; init; } = [];
}
