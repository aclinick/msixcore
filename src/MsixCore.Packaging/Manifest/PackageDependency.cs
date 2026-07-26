namespace MsixCore.Packaging.Manifest;

/// <summary>The kind of package relationship a <see cref="PackageDependency"/> expresses.</summary>
public enum PackageDependencyKind
{
    /// <summary>
    /// A framework or resource package the app needs at runtime (<c>PackageDependency</c>), such as
    /// <c>Microsoft.VCLibs.140.00</c>. Resolved against packages already present on the machine.
    /// </summary>
    Framework,

    /// <summary>
    /// The package this one modifies (<c>uap3:MainPackageDependency</c> /
    /// <c>uap4:MainPackageDependency</c>). Declared by a modification package, which cannot be
    /// deployed on its own.
    /// </summary>
    MainPackage,

    /// <summary>
    /// The host runtime that executes this package's code (<c>uap10:HostRuntimeDependency</c> /
    /// <c>uap13:HostRuntimeDependency</c>), used by hosted apps that ship no executable of their own.
    /// </summary>
    HostRuntime,
}

/// <summary>
/// A dependency on another package declared under <c>Dependencies</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three kinds share a shape but not a meaning, so <see cref="Kind"/> is carried explicitly
/// rather than inferred by the consumer: treating a <see cref="PackageDependencyKind.MainPackage"/>
/// entry as a framework would make a modification package look independently deployable.
/// </para>
/// <para>
/// Which attributes the schema requires differs per kind, which is why <see cref="Publisher"/> and
/// <see cref="MinVersion"/> are nullable here even though both are required on a foundation
/// <c>PackageDependency</c>. See <c>docs/manifest-dependencies.md</c> for the per-kind table.
/// </para>
/// </remarks>
public sealed record PackageDependency
{
    /// <summary>Which relationship this dependency expresses.</summary>
    public required PackageDependencyKind Kind { get; init; }

    /// <summary>The dependency's package name (the <c>Name</c> attribute). Required for every kind.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The publisher the dependency must be signed by, or <see langword="null"/> when not declared.
    /// </summary>
    /// <remarks>
    /// Required by the schema for <see cref="PackageDependencyKind.Framework"/> and
    /// <see cref="PackageDependencyKind.HostRuntime"/>; optional for
    /// <see cref="PackageDependencyKind.MainPackage"/>, whose <c>uap3</c> form has no
    /// <c>Publisher</c> attribute at all.
    /// </remarks>
    public string? Publisher { get; init; }

    /// <summary>
    /// The lowest acceptable version of the dependency, or <see langword="null"/> when the element
    /// carries no version at all.
    /// </summary>
    /// <remarks>
    /// Required by the schema for <see cref="PackageDependencyKind.Framework"/> and
    /// <see cref="PackageDependencyKind.HostRuntime"/>. Always <see langword="null"/> for
    /// <see cref="PackageDependencyKind.MainPackage"/>, which has no version attribute at all: a
    /// modification package binds to its parent by name and publisher only. It is left null rather
    /// than defaulted to <c>0.0.0.0</c> so that "no version constraint exists" stays distinguishable
    /// from "version zero was explicitly requested".
    /// </remarks>
    public Version? MinVersion { get; init; }

    /// <summary>
    /// The highest <em>major</em> version the package was tested against
    /// (<c>MaxMajorVersionTested</c>), or <see langword="null"/> when not declared.
    /// </summary>
    /// <remarks>
    /// This is a single unsigned 16-bit major-version number, <em>not</em> a four-part version quad,
    /// and only foundation <c>PackageDependency</c> declares it. Combined with
    /// <see cref="MinVersion"/> it expresses the acceptable range
    /// <c>[MinVersion, MaxMajorVersionTested + 1)</c>.
    /// </remarks>
    public ushort? MaxMajorVersionTested { get; init; }

    /// <summary>
    /// Whether the dependency is declared optional (<c>uap6:Optional="true"</c>), meaning the
    /// package may be installed and run without it.
    /// </summary>
    /// <remarks>
    /// Only foundation <c>PackageDependency</c> carries this attribute. An optional dependency is
    /// still reported by resolution, but its absence does not block deployment.
    /// </remarks>
    public bool IsOptional { get; init; }
}
