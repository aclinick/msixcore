namespace MsixCore.Packaging.Integrity;

/// <summary>The verification status of a single APPX digest entry.</summary>
public enum DigestVerificationStatus
{
    /// <summary>The digest was verified and matches the package content.</summary>
    Valid,

    /// <summary>The digest does not match the package content.</summary>
    Mismatch,

    /// <summary>The digest tag is present but the corresponding package part is missing.</summary>
    PartMissing,

    /// <summary>The part exists in the package but the digest table has no entry for it.</summary>
    DigestMissing,

    /// <summary>Verification of this tag is not supported (AXPC/AXCD — exact byte ranges are unrecoverable).</summary>
    NotVerified,
}

/// <summary>The verification result for a single digest entry.</summary>
public sealed record DigestEntryResult
{
    /// <summary>The digest tag.</summary>
    public required AppxDigestTag Tag { get; init; }

    /// <summary>The outcome of verification.</summary>
    public required DigestVerificationStatus Status { get; init; }

    /// <summary>Optional detail message (e.g. reason for <see cref="DigestVerificationStatus.NotVerified"/>).</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Structured result of verifying the APPX indirect-data digest binding between a CMS
/// signature and the package contents it protects.
/// </summary>
public sealed record IndirectDataBindingResult
{
    /// <summary>
    /// Overall binding verdict. <see langword="true"/> only when all verifiable digests
    /// (AXCT, AXBM, and — if present — AXCI) match. AXPC and AXCD being <see cref="DigestVerificationStatus.NotVerified"/>
    /// does not cause this to be <see langword="false"/>.
    /// </summary>
    public required bool IsBindingValid { get; init; }

    /// <summary>Per-tag verification results.</summary>
    public required IReadOnlyList<DigestEntryResult> Results { get; init; }

    /// <summary>Human-readable summary of the binding state.</summary>
    public required string Summary { get; init; }
}
