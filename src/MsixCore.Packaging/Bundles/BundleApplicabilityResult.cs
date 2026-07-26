using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Bundles;

/// <summary>The packages from a bundle that apply to a given <see cref="BundleTarget"/>.</summary>
public sealed record BundleApplicabilityResult
{
    /// <summary>
    /// The single application package that should be installed. Exactly one is chosen even when
    /// several architectures are runnable, because installing two app packages from one bundle is
    /// never correct.
    /// </summary>
    public required BundlePackageEntry ApplicationPackage { get; init; }

    /// <summary>
    /// The application packages that could have run on the target, best first. Present so callers
    /// can explain <i>why</i> a package was chosen; only <see cref="ApplicationPackage"/> should be
    /// installed.
    /// </summary>
    public IReadOnlyList<BundlePackageEntry> CandidateApplicationPackages { get; init; } = [];

    /// <summary>The resource packages that apply, in bundle-manifest order.</summary>
    public IReadOnlyList<BundlePackageEntry> ResourcePackages { get; init; } = [];

    /// <summary>
    /// Every package to install: <see cref="ApplicationPackage"/> followed by
    /// <see cref="ResourcePackages"/>.
    /// </summary>
    public IReadOnlyList<BundlePackageEntry> ApplicablePackages =>
        [ApplicationPackage, .. ResourcePackages];
}
