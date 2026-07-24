namespace MsixCore.Deployment;

/// <summary>
/// Options that control how a package is added/deployed. Mirrors (and extends) the native
/// <c>DeploymentOptions</c> enum. Flags so future options compose.
/// </summary>
[Flags]
public enum DeploymentOptions
{
    /// <summary>Default deployment behavior.</summary>
    None = 0,

    /// <summary>Allow installing over an existing package regardless of version.</summary>
    ForceApplicationShutdown = 1 << 0,

    /// <summary>Skip OS-integration handlers (shortcuts, registry, associations); extract only.</summary>
    ExtractOnly = 1 << 1,
}
