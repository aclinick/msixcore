namespace MsixCore.PackageStore;

/// <summary>
/// Coarse progress stages reported during an add/remove operation.
/// </summary>
/// <remarks>
/// This is msixcore's own progress model (surfaced via <see cref="IMsixResponse"/> events), inspired
/// by the native <c>InstallationStep</c> enum but intentionally not binary-compatible with it: the
/// values are ordered to reflect the deployment sequence, and <see cref="Integration"/> is a new
/// stage that has no native counterpart.
/// </remarks>
public enum InstallationStep
{
    /// <summary>Initial/unknown state.</summary>
    Unknown = 0,

    /// <summary>The operation has started.</summary>
    Started,

    /// <summary>Reading and validating package information.</summary>
    GetPackageInformation,

    /// <summary>Extracting package payload to the install location.</summary>
    Extraction,

    /// <summary>Applying OS-integration handlers.</summary>
    Integration,

    /// <summary>The operation completed successfully.</summary>
    Completed,

    /// <summary>The operation failed; see the response error details.</summary>
    Error,
}
