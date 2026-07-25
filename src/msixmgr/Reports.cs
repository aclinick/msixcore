using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsixMgr;

/// <summary>Machine-readable result of the <c>inspect</c> verb.</summary>
internal sealed record InspectionReport
{
    public required string Name { get; init; }

    public required string PackageFullName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required string DisplayName { get; init; }

    public required string PublisherDisplayName { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }

    public required bool IsSigned { get; init; }

    public int? BlockMapFileCount { get; init; }

    public string? BlockMapHashMethod { get; init; }
}

/// <summary>Machine-readable result of the <c>validate</c> verb.</summary>
internal sealed record ValidationReport
{
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
    /// Always <see langword="false"/> today: this tool does not yet verify that the signature's APPX
    /// indirect-data digests bind it to this package's block map/manifest. Until it does, a valid
    /// signature does not prove the payload is the one that was signed.
    /// </summary>
    public bool? SignatureBindingVerified { get; init; }

    /// <summary>
    /// Always <see langword="false"/> today: certificate trust-chain evaluation is environment- and
    /// policy-dependent and is intentionally not performed here.
    /// </summary>
    public bool? SignatureTrustVerified { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Machine-readable result of the <c>unpack</c> verb.</summary>
internal sealed record UnpackReport
{
    public required string Destination { get; init; }

    public required int ExtractedPartCount { get; init; }
}

/// <summary>Shared JSON options for CLI report serialization.</summary>
internal static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
