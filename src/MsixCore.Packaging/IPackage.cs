namespace MsixCore.Packaging;

/// <summary>
/// Read-only view over an MSIX package's metadata, independent of where the package lives
/// (a <c>.msix</c>/<c>.appx</c> file, a stream, or an installed layout on disk).
/// </summary>
/// <remarks>
/// This is the C# analogue of the native <c>MsixCoreLib::IPackage</c> interface, reshaped to
/// idiomatic .NET (properties instead of getter methods, exceptions instead of <c>HRESULT</c>).
/// A package may own an underlying OPC/ZIP reader (file handle or stream), so it is
/// <see cref="IDisposable"/>; callers should dispose it (e.g. with <c>using</c>).
/// </remarks>
public interface IPackage : IDisposable
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
