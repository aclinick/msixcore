namespace MsixCore.Deployment;

/// <summary>
/// Coarse progress stages reported during an add/remove operation.
/// Mirrors the native <c>InstallationStep</c> enum.
/// </summary>
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
