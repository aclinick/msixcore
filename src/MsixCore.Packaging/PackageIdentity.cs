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
    /// The package family name: <c>{Name}_{publisherHash}</c> where the publisher hash is the
    /// Base32 (Crockford-style, MSIX variant) encoding of the first 8 bytes of the SHA-256 of the
    /// UTF-16LE publisher string. Computed in Phase 1.
    /// </summary>
    public string PackageFamilyName =>
        throw new NotImplementedException("PackageFamilyName is implemented in Phase 1.");

    /// <summary>
    /// The package full name:
    /// <c>{Name}_{Version}_{Architecture}_{ResourceId}_{publisherHash}</c>. Computed in Phase 1.
    /// </summary>
    public string PackageFullName =>
        throw new NotImplementedException("PackageFullName is implemented in Phase 1.");
}
