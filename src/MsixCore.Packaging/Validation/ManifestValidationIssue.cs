namespace MsixCore.Packaging.Validation;

/// <summary>How seriously a <see cref="ManifestValidationIssue"/> should be taken.</summary>
public enum ManifestValidationSeverity
{
    /// <summary>
    /// The manifest is questionable but not provably wrong — typically because it uses something
    /// this library does not recognise. A warning never makes a result invalid.
    /// </summary>
    Warning,

    /// <summary>The manifest violates a rule Windows enforces. The package will not deploy.</summary>
    Error,
}

/// <summary>
/// The rule a <see cref="ManifestValidationIssue"/> reports. These are stable identifiers intended
/// for scripting and suppression; new members may be added, but existing ones will not be renamed.
/// </summary>
public enum ManifestValidationRule
{
    /// <summary>An identifier contains characters outside <c>[-.A-Za-z0-9]</c>.</summary>
    IdentifierMalformed,

    /// <summary>
    /// An identifier is a reserved DOS device name (<c>con</c>, <c>lpt1</c>, …), a bare <c>.</c> or
    /// <c>..</c>, starts with a reserved prefix, or ends with a period. These are rejected because
    /// package identifiers become directory names on disk.
    /// </summary>
    IdentifierReserved,

    /// <summary>An identifier is shorter or longer than the schema allows.</summary>
    IdentifierLength,

    /// <summary>The <c>Publisher</c> is not a well-formed X.500 distinguished name.</summary>
    PublisherMalformed,

    /// <summary>
    /// A version range is inverted — a dependency's minimum version exceeds the maximum version the
    /// package was tested against.
    /// </summary>
    VersionRangeInverted,

    /// <summary>The package declares itself both a framework and a resource package.</summary>
    ConflictingPackageType,

    /// <summary>A framework package declares content a framework may not contain.</summary>
    FrameworkContent,

    /// <summary>A resource package declares content a resource package may not contain.</summary>
    ResourcePackageContent,

    /// <summary>An optional (modification) package declares content it may not contain.</summary>
    OptionalPackageContent,

    /// <summary>An <c>Application/@Id</c> does not match the schema's identifier form.</summary>
    ApplicationIdMalformed,

    /// <summary>Two applications share an <c>Id</c>.</summary>
    DuplicateApplicationId,

    /// <summary>The same capability is declared more than once.</summary>
    DuplicateCapability,

    /// <summary>
    /// The manifest uses an XML namespace that is not in the known schema registry. Reported as a
    /// warning, not an error: a package built against a newer Windows SDK than this library knows
    /// about is valid, and failing it would be wrong.
    /// </summary>
    UnknownNamespace,
}

/// <summary>A single problem found in a manifest.</summary>
public sealed record ManifestValidationIssue
{
    /// <summary>How seriously to take this issue.</summary>
    public required ManifestValidationSeverity Severity { get; init; }

    /// <summary>The rule that was violated.</summary>
    public required ManifestValidationRule Rule { get; init; }

    /// <summary>
    /// What the issue is about — an element or attribute path such as <c>Identity/@Name</c> or
    /// <c>Dependencies/PackageDependency[Microsoft.VCLibs.140.00]</c>.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>A human-readable explanation.</summary>
    public required string Message { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Severity} {Rule} at {Target}: {Message}";
}
