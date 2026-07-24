namespace MsixCore.Packaging;

/// <summary>
/// The immutable identity of an MSIX package, as declared in the
/// <c>&lt;Identity&gt;</c> element of its <c>AppxManifest.xml</c>.
/// </summary>
/// <remarks>
/// This type is populated in Phase 1 (identity + <see cref="PackageFullName"/> /
/// <see cref="PackageFamilyName"/> computation). It is defined here in Phase 0 so the
/// public surface of the library is stable while implementation lands incrementally.
/// </remarks>
public sealed record PackageIdentity
{
    /// <summary>The package <c>Name</c> attribute (e.g. <c>Contoso.MyApp</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The full <c>Publisher</c> distinguished name (e.g. <c>CN=Contoso, O=Contoso</c>).</summary>
    public required string Publisher { get; init; }

    /// <summary>The four-part package version.</summary>
    public required Version Version { get; init; }

    /// <summary>The declared processor architecture.</summary>
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.Neutral;

    /// <summary>Optional <c>ResourceId</c> for resource packages; empty for the main package.</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>
    /// The package family name: <c>{Name}_{publisherHash}</c>, where the publisher hash is computed
    /// by <see cref="PublisherHash.Compute(string)"/>.
    /// </summary>
    public string PackageFamilyName => $"{Name}_{PublisherHash.Compute(Publisher)}";

    /// <summary>
    /// The package full name:
    /// <c>{Name}_{Version}_{Architecture}_{ResourceId}_{publisherHash}</c>. The <c>ResourceId</c>
    /// segment is empty for the main package, producing the customary double underscore.
    /// </summary>
    public string PackageFullName =>
        $"{Name}_{FormatVersion(Version)}_{ArchitectureMoniker(Architecture)}_{ResourceId}_{PublisherHash.Compute(Publisher)}";

    /// <summary>The lowercase architecture moniker used in the package full name (e.g. <c>x64</c>, <c>neutral</c>).</summary>
    public static string ArchitectureMoniker(ProcessorArchitecture architecture) => architecture switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        ProcessorArchitecture.Arm => "arm",
        ProcessorArchitecture.Arm64 => "arm64",
        ProcessorArchitecture.X86OnArm64 => "x86a64",
        ProcessorArchitecture.Neutral => "neutral",
        _ => "unknown",
    };

    private static string FormatVersion(Version version)
    {
        int major = Math.Max(version.Major, 0);
        int minor = Math.Max(version.Minor, 0);
        int build = Math.Max(version.Build, 0);
        int revision = Math.Max(version.Revision, 0);
        return $"{major}.{minor}.{build}.{revision}";
    }
}
