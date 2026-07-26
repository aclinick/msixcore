using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixCore.PackageStore;

/// <summary>Why a single declared dependency could or could not be satisfied.</summary>
public enum DependencyResolutionStatus
{
    /// <summary>An installed package satisfies the dependency.</summary>
    Resolved,

    /// <summary>No package in the dependency's family is installed.</summary>
    NotInstalled,

    /// <summary>
    /// A package in the family is installed, and new enough, but it is not a framework package —
    /// so it cannot satisfy a <c>PackageDependency</c>, which references a framework.
    /// </summary>
    NotAFramework,

    /// <summary>
    /// A package in the family is installed but its version is older than the declared
    /// <see cref="PackageDependency.MinVersion"/>.
    /// </summary>
    VersionTooLow,
}

/// <summary>The outcome of resolving one declared dependency against a package store.</summary>
/// <param name="Dependency">The dependency as declared in the manifest.</param>
/// <param name="Status">Whether, and why, the dependency was satisfied.</param>
/// <param name="ResolvedPackage">
/// The installed package that satisfied the dependency, or the best candidate found when
/// <paramref name="Status"/> is <see cref="DependencyResolutionStatus.VersionTooLow"/>.
/// <see langword="null"/> when nothing in the family is installed.
/// </param>
public sealed record DependencyResolution(
    PackageDependency Dependency,
    DependencyResolutionStatus Status,
    InstalledPackageInfo? ResolvedPackage)
{
    /// <summary>Whether this dependency is satisfied.</summary>
    public bool IsSatisfied => Status == DependencyResolutionStatus.Resolved;

    /// <summary>A human-readable explanation, used in deployment failure messages.</summary>
    public string Describe()
    {
        string kind = Dependency.Kind switch
        {
            PackageDependencyKind.Framework => "framework",
            PackageDependencyKind.MainPackage => "main package",
            PackageDependencyKind.HostRuntime => "host runtime",
            _ => "package",
        };

        return Status switch
        {
            DependencyResolutionStatus.Resolved =>
                $"{kind} '{Dependency.Name}' is satisfied by {ResolvedPackage?.Identity.PackageFullName}.",
            DependencyResolutionStatus.VersionTooLow =>
                $"{kind} '{Dependency.Name}' requires version {Dependency.MinVersion} or later, "
                + $"but only {ResolvedPackage?.Identity.Version} is installed.",
            DependencyResolutionStatus.NotAFramework =>
                $"{kind} '{Dependency.Name}' is installed but is not a framework package, "
                + "so it cannot satisfy a framework dependency.",
            _ => $"{kind} '{Dependency.Name}' is not installed.",
        };
    }
}

/// <summary>The outcome of resolving every dependency a package declares.</summary>
/// <param name="Resolutions">One entry per declared dependency, in manifest order.</param>
public sealed record DependencyResolutionResult(IReadOnlyList<DependencyResolution> Resolutions)
{
    /// <summary>Whether every declared dependency is satisfied.</summary>
    public bool IsSatisfied => Resolutions.All(static resolution => resolution.IsSatisfied);

    /// <summary>The dependencies that are not satisfied, in manifest order.</summary>
    public IReadOnlyList<DependencyResolution> Unsatisfied =>
        Resolutions.Where(static resolution => !resolution.IsSatisfied).ToList();

    /// <summary>
    /// The unsatisfied dependencies that actually block deployment — that is, excluding those
    /// declared <c>uap6:Optional="true"</c>, which the package can run without.
    /// </summary>
    public IReadOnlyList<DependencyResolution> Blocking =>
        Resolutions
            .Where(static resolution => !resolution.IsSatisfied && !resolution.Dependency.IsOptional)
            .ToList();

    /// <summary>Whether deployment can proceed: every <em>required</em> dependency is satisfied.</summary>
    public bool CanDeploy => Blocking.Count == 0;
}

/// <summary>
/// Resolves the <c>Dependencies</c> a package declares against the packages already present in a
/// store.
/// </summary>
/// <remarks>
/// <para>
/// A dependency names a package by <c>Name</c> plus <c>Publisher</c>, which together determine the
/// package family name — so resolution is a family lookup, not a full-name lookup. A
/// <c>MainPackageDependency</c> may omit <c>Publisher</c>, in which case the modification package's
/// own publisher is used: a modification package and the package it modifies are required to share
/// a publisher, which is exactly why the schema lets the attribute be omitted.
/// </para>
/// <para>
/// Architecture is checked as well as version. A framework built for a different architecture cannot
/// load into the app's process, so an installed x86 framework does not satisfy an x64 app. A
/// <c>neutral</c> package on either side matches anything, since neutral packages carry no
/// architecture-specific code.
/// </para>
/// </remarks>
public static class DependencyResolver
{
    /// <summary>Resolves every dependency declared by <paramref name="manifest"/>.</summary>
    /// <param name="manifest">The manifest of the package being deployed.</param>
    /// <param name="store">The store whose installed packages can satisfy the dependencies.</param>
    /// <returns>One resolution per declared dependency, in manifest order.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static DependencyResolutionResult Resolve(AppxManifest manifest, IPackageStore store)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(store);

        var resolutions = new List<DependencyResolution>(manifest.PackageDependencies.Count);
        foreach (PackageDependency dependency in manifest.PackageDependencies)
        {
            resolutions.Add(ResolveOne(dependency, manifest.Identity, store));
        }

        return new DependencyResolutionResult(resolutions);
    }

    private static DependencyResolution ResolveOne(
        PackageDependency dependency,
        PackageIdentity dependent,
        IPackageStore store)
    {
        // MainPackageDependency may omit Publisher; a modification package always shares its
        // parent's publisher, so the dependent's own publisher is the correct fallback.
        string publisher = dependency.Publisher ?? dependent.Publisher;
        string familyName = PackageIdentity.ComputeFamilyName(dependency.Name, publisher);

        // Enumerate rather than using FindByFamilyName so that a store holding several
        // architecture-specific packages in one family (the normal shape for frameworks) can be
        // filtered by architecture before the version comparison.
        List<InstalledPackageInfo> candidates = store.EnumeratePackages()
            .Where(installed => string.Equals(
                installed.Identity.PackageFamilyName,
                familyName,
                StringComparison.Ordinal))
            .Where(installed => IsArchitectureCompatible(installed.Identity.Architecture, dependent.Architecture))
            .OrderByDescending(static installed => installed.Identity.Version)
            .ToList();

        if (candidates.Count == 0)
        {
            return new DependencyResolution(dependency, DependencyResolutionStatus.NotInstalled, null);
        }

        // A PackageDependency references a framework package. An ordinary app package that happens
        // to share the family name must not satisfy it. The other two kinds intentionally have no
        // role constraint: the package a modification package modifies, and a host runtime, are both
        // ordinary packages.
        if (dependency.Kind == PackageDependencyKind.Framework)
        {
            List<InstalledPackageInfo> frameworks = candidates
                .Where(static installed => installed.IsFramework)
                .ToList();
            if (frameworks.Count == 0)
            {
                return new DependencyResolution(
                    dependency,
                    DependencyResolutionStatus.NotAFramework,
                    candidates[0]);
            }

            candidates = frameworks;
        }

        InstalledPackageInfo best = candidates[0];
        if (dependency.MinVersion is not null && best.Identity.Version < dependency.MinVersion)
        {
            return new DependencyResolution(dependency, DependencyResolutionStatus.VersionTooLow, best);
        }

        return new DependencyResolution(dependency, DependencyResolutionStatus.Resolved, best);
    }

    private static bool IsArchitectureCompatible(
        ProcessorArchitecture dependency,
        ProcessorArchitecture dependent) =>
        dependency == ProcessorArchitecture.Neutral
        || dependent == ProcessorArchitecture.Neutral
        || dependency == dependent;
}
