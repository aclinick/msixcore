namespace MsixCore.Packaging;

/// <summary>
/// Read-only view over an MSIX package's metadata, independent of where the package lives
/// (a <c>.msix</c>/<c>.appx</c> file, a stream, or an installed layout on disk).
/// </summary>
/// <remarks>
/// This is the C# analogue of the native <c>MsixCoreLib::IPackage</c> interface, reshaped to
/// idiomatic .NET (properties instead of getter methods, exceptions instead of <c>HRESULT</c>).
/// </remarks>
public interface IPackage
{
    /// <summary>The package identity (name, publisher, version, architecture).</summary>
    PackageIdentity Identity { get; }

    /// <summary>The user-facing display name (from <c>Properties/DisplayName</c>).</summary>
    string DisplayName { get; }

    /// <summary>The publisher display name (from <c>Properties/PublisherDisplayName</c>).</summary>
    string PublisherDisplayName { get; }

    /// <summary>The declared capabilities (e.g. <c>runFullTrust</c>, <c>internetClient</c>).</summary>
    IReadOnlyList<string> Capabilities { get; }

    /// <summary>Opens the package logo as a stream, or <see langword="null"/> if none is declared.</summary>
    /// <returns>A readable, seekable stream the caller owns and must dispose, or <see langword="null"/>.</returns>
    Stream? OpenLogo();
}

/// <summary>
/// An <see cref="IPackage"/> that has been extracted/installed to a location on disk.
/// </summary>
public interface IInstalledPackage : IPackage
{
    /// <summary>The absolute path of the installed package root.</summary>
    string InstalledLocation { get; }

    /// <summary>The resolved entry-point execution info, or <see langword="null"/> if the package has no app.</summary>
    ExecutionInfo? ExecutionInfo { get; }
}

/// <summary>Resolved information required to launch a package's primary application.</summary>
public sealed record ExecutionInfo
{
    /// <summary>Absolute path to the executable to launch.</summary>
    public required string ResolvedExecutableFilePath { get; init; }

    /// <summary>Command-line arguments to pass, if any.</summary>
    public string CommandLineArguments { get; init; } = string.Empty;

    /// <summary>Working directory for the process, if specified.</summary>
    public string WorkingDirectory { get; init; } = string.Empty;
}
