using System.Text.Json.Serialization;

namespace MsixKit;

/// <summary>Machine-readable result of the <c>inspect</c> verb.</summary>
internal sealed record InspectionReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    public required string Name { get; init; }

    public required string PackageFullName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required string DisplayName { get; init; }

    public required string PublisherDisplayName { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// The declared package-to-package dependencies. Additive to schema version 1: consumers that
    /// predate it simply ignore the property.
    /// </summary>
    public IReadOnlyList<DependencyReport> Dependencies { get; init; } = [];

    /// <summary>
    /// The declared OS integration points, from both the package-level and the per-application
    /// <c>Extensions</c> containers. Additive to schema version 1.
    /// </summary>
    public IReadOnlyList<ExtensionReport> Extensions { get; init; } = [];

    public required bool IsSigned { get; init; }

    public int? BlockMapFileCount { get; init; }

    public string? BlockMapHashMethod { get; init; }
}

/// <summary>One declared <c>Extension</c>, as reported by <c>inspect</c>.</summary>
internal sealed record ExtensionReport
{
    /// <summary>
    /// The id of the declaring application, or <see langword="null"/> for a package-level
    /// extension. Package-level extensions have no owning application, so the property is nullable
    /// rather than defaulted to an empty string.
    /// </summary>
    public string? ApplicationId { get; init; }

    /// <summary>The category string, e.g. <c>windows.fileTypeAssociation</c>.</summary>
    public required string Category { get; init; }

    public string? Executable { get; init; }

    /// <summary>
    /// A one-line summary of the category's payload — the associated extensions, the protocol
    /// name, the aliases, and so on. <see langword="null"/> for a category msixcore does not model
    /// or for an extension that declares no child element.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>One declared <c>Dependencies</c> entry, as reported by <c>inspect</c>.</summary>
internal sealed record DependencyReport
{
    /// <summary>One of <c>framework</c>, <c>mainPackage</c>, or <c>hostRuntime</c>.</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    public string? Publisher { get; init; }

    public string? MinVersion { get; init; }

    public int? MaxMajorVersionTested { get; init; }

    /// <summary>Whether the dependency is declared <c>uap6:Optional</c>.</summary>
    public bool IsOptional { get; init; }
}

/// <summary>Machine-readable result of the <c>validate</c> verb.</summary>
internal sealed record ValidationReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    public required string PackageFullName { get; init; }

    /// <summary>
    /// Whether the package passed the checks this tool performs today: block-map hash/coverage
    /// integrity and, when signed, CMS envelope integrity and signer/Publisher agreement. This is
    /// <em>not</em> an authenticity verdict — see <see cref="SignatureBindingVerified"/>.
    /// </summary>
    public required bool IsValid { get; init; }

    public required bool BlockMapValid { get; init; }

    public int VerifiedFileCount { get; init; }

    public required bool IsSigned { get; init; }

    public bool? CmsIntegrityValid { get; init; }

    /// <summary>
    /// Whether the signature's APPX indirect-data digests bind to the package contents.
    /// <see langword="true"/> when AXCT, AXBM, and (if present) AXCI digests match.
    /// AXPC and AXCD are present but not verified (exact ZIP byte ranges are not recoverable from public spec).
    /// <see langword="false"/> when binding verification failed.
    /// <see langword="null"/> for unsigned packages or when the digest table is unavailable.
    /// </summary>
    public bool? SignatureBindingVerified { get; init; }

    /// <summary>
    /// Always <see langword="false"/> today: certificate trust-chain evaluation is environment- and
    /// policy-dependent and is intentionally not performed here.
    /// </summary>
    public bool? SignatureTrustVerified { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Per-tag binding digest verification details (when the signature has a digest table).</summary>
    public IReadOnlyList<BindingDigestReport>? BindingDigests { get; init; }
}

/// <summary>Machine-readable status of a single APPX digest tag from the signature binding.</summary>
internal sealed record BindingDigestReport
{
    /// <summary>The tag name (e.g. "Axbm", "Axct").</summary>
    public required string Tag { get; init; }

    /// <summary>The verification status (e.g. "Valid", "Mismatch", "NotVerified").</summary>
    public required string Status { get; init; }

    /// <summary>Optional detail about the verification outcome.</summary>
    public string? Detail { get; init; }
}

/// <summary>Machine-readable result of the <c>unpack</c> verb.</summary>
internal sealed record UnpackReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    public required string Destination { get; init; }

    public required int ExtractedPartCount { get; init; }
}

/// <summary>Machine-readable result of the <c>pack</c> verb.</summary>
internal sealed record PackReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    public required string OutputPath { get; init; }

    public required string Name { get; init; }

    public required string PackageFullName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required int FileCount { get; init; }

    public required long TotalSize { get; init; }

    public required bool IsSigned { get; init; }

    public required string Compression { get; init; }
}

/// <summary>Machine-readable result of the <c>bundle</c> verb.</summary>
internal sealed record BundleReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    public required string OutputPath { get; init; }

    public required string Name { get; init; }

    public required string PackageFullName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string Version { get; init; }

    public required int PackageCount { get; init; }

    public required long TotalSize { get; init; }

    public required bool IsSigned { get; init; }

    public required IReadOnlyList<BundlePackageReport> Packages { get; init; }
}

/// <summary>Machine-readable child-package entry in a bundle report.</summary>
internal sealed record BundlePackageReport
{
    public required string FileName { get; init; }

    public required string Type { get; init; }

    public required string Version { get; init; }

    public string? Architecture { get; init; }

    public string? ResourceId { get; init; }

    public required long Offset { get; init; }

    public required long Size { get; init; }
}

/// <summary>Machine-readable error result emitted under <c>--json</c>.</summary>
internal sealed record ErrorReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CliContract.SchemaVersion;

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>Source-generated JSON metadata for CLI reports.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InspectionReport))]
[JsonSerializable(typeof(ValidationReport))]
[JsonSerializable(typeof(UnpackReport))]
[JsonSerializable(typeof(PackReport))]
[JsonSerializable(typeof(BundleReport))]
[JsonSerializable(typeof(ErrorReport))]
[JsonSerializable(typeof(BindingDigestReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
}
