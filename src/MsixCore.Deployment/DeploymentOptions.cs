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

    /// <summary>Allow deployment handlers to shut down applications that are using package files.</summary>
    ForceApplicationShutdown = 1 << 0,

    /// <summary>Skip OS-integration handlers (shortcuts, registry, associations); extract only.</summary>
    ExtractOnly = 1 << 1,

    /// <summary>Replace an already-installed package with the same full name and version.</summary>
    ForceReinstall = 1 << 2,

    /// <summary>Allow an older version to replace the currently installed package family.</summary>
    AllowDowngrade = 1 << 3,
}
