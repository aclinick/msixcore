using MsixCore.Packaging;

namespace MsixCore.Deployment.Handlers;

/// <summary>
/// A single unit of work in the add/remove pipeline (e.g. extraction, Start Menu shortcut,
/// Add/Remove Programs registration). Handlers run in a defined order on add and in reverse on
/// remove. The C# analogue of the native <c>IPackageHandler</c>.
/// </summary>
public interface IPackageHandler
{
    /// <summary>A stable identifier used for ordering, logging, and diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns <see langword="true"/> if this handler is applicable on the current platform/package.</summary>
    /// <param name="context">The deployment context.</param>
    bool IsApplicable(PackageDeploymentContext context);

    /// <summary>Performs this handler's work as part of adding (installing) the package.</summary>
    /// <param name="context">The deployment context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAddAsync(PackageDeploymentContext context, CancellationToken cancellationToken = default);

    /// <summary>Reverses this handler's work as part of removing (uninstalling) the package.</summary>
    /// <param name="context">The deployment context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteRemoveAsync(PackageDeploymentContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared state threaded through the handler pipeline for a single add/remove operation.
/// Fleshed out in Phase 5.
/// </summary>
public sealed class PackageDeploymentContext
{
    /// <summary>The package being deployed or removed.</summary>
    public required IPackage Package { get; init; }

    /// <summary>The absolute install root for the package.</summary>
    public required string InstallLocation { get; init; }

    /// <summary>The options for this operation.</summary>
    public DeploymentOptions Options { get; init; } = DeploymentOptions.None;
}
