namespace MsixCore.Packaging.Bundles;

/// <summary>Qualifiers to ignore when resolving which bundle packages apply to a target.</summary>
/// <remarks>
/// <para>
/// These values intentionally do <b>not</b> mirror the upstream SDK's
/// <c>MSIX_APPLICABILITY_OPTIONS</c> numeric values. Upstream defines
/// <c>SKIPPLATFORM = 1</c> and <c>SKIPLANGUAGE = 2</c>, but its platform filtering is commented out,
/// so <c>SKIPPLATFORM</c> is a no-op there and has no counterpart here. Reusing its numbers would
/// imply a compatibility that does not exist.
/// </para>
/// <para>
/// Combine with <see cref="All"/> to extract every package in a bundle, which is the closest
/// equivalent to upstream's <c>MSIX_APPLICABILITY_NONE</c>.
/// </para>
/// </remarks>
[Flags]
public enum BundleApplicabilityOptions
{
    /// <summary>Apply every qualifier the target specifies.</summary>
    None = 0,

    /// <summary>Select application packages regardless of architecture.</summary>
    SkipArchitecture = 1 << 0,

    /// <summary>Select language resource packages regardless of language.</summary>
    SkipLanguage = 1 << 1,

    /// <summary>Select scale resource packages regardless of scale.</summary>
    SkipScale = 1 << 2,

    /// <summary>Select DirectX resource packages regardless of feature level.</summary>
    SkipDXFeatureLevel = 1 << 3,

    /// <summary>
    /// Ignore every qualifier, so no resource package is filtered out and any application package
    /// is considered runnable.
    /// </summary>
    /// <remarks>
    /// This still yields exactly one <see cref="BundleApplicabilityResult.ApplicationPackage"/>;
    /// see <see cref="BundleApplicabilityResult.CandidateApplicationPackages"/> for the rest. A
    /// bundle's application packages are alternatives to each other, so "install them all" is never
    /// a meaningful request even when no qualifier is being applied.
    /// </remarks>
    All = SkipArchitecture | SkipLanguage | SkipScale | SkipDXFeatureLevel,
}
