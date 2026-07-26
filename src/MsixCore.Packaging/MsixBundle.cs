using MsixCore.Packaging.Bundles;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging;

/// <summary>The primary entry point for reading an MSIX/APPX bundle.</summary>
public sealed class MsixBundle : IDisposable
{
    private readonly IOpcPackage _opc;
    private readonly Lazy<BundleManifest> _manifest;
    private bool _disposed;

    private MsixBundle(IOpcPackage opc)
    {
        _opc = opc;
        _manifest = new Lazy<BundleManifest>(ReadManifest);
    }

    /// <summary>The underlying bundle OPC/ZIP container.</summary>
    public IOpcPackage Opc
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _opc;
        }
    }

    /// <summary>The parsed <c>AppxBundleManifest.xml</c>.</summary>
    public BundleManifest Manifest
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _manifest.Value;
        }
    }

    /// <summary>The bundle identity.</summary>
    public PackageIdentity Identity => Manifest.Identity;

    /// <summary>The package entries contained in the bundle.</summary>
    public IReadOnlyList<BundlePackageEntry> Packages => Manifest.Packages;

    /// <summary>
    /// Selects the packages in this bundle that apply to a target device.
    /// </summary>
    /// <param name="target">The device context to resolve against; defaults to the current device.</param>
    /// <param name="options">Qualifiers to ignore.</param>
    /// <returns>The applicable application package and resource packages.</returns>
    /// <exception cref="InvalidDataException">
    /// The bundle contains no application package applicable to the target.
    /// </exception>
    public BundleApplicabilityResult SelectApplicable(
        BundleTarget? target = null,
        BundleApplicabilityOptions options = BundleApplicabilityOptions.None) =>
        BundleApplicability.Select(Manifest, target ?? BundleTarget.Current(), options);

    /// <summary>Opens an MSIX/APPX bundle from a file path.</summary>
    public static MsixBundle Open(string path) => Create(OpcPackage.Open(path));

    /// <summary>Opens an MSIX/APPX bundle from a seekable stream.</summary>
    public static MsixBundle Open(Stream stream, bool leaveOpen = false) =>
        Create(OpcPackage.Open(stream, leaveOpen));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _opc.Dispose();
    }

    private static MsixBundle Create(OpcPackage opc)
    {
        if (!opc.ContainsPart(OpcPartNames.AppxBundleManifest))
        {
            opc.Dispose();
            throw new MsixPackageTypeException(
                $"The container does not contain '{OpcPartNames.AppxBundleManifest}' and is not an MSIX bundle.");
        }

        return new MsixBundle(opc);
    }

    private BundleManifest ReadManifest()
    {
        using Stream manifest = _opc.OpenPart(OpcPartNames.AppxBundleManifest);
        return BundleManifestParser.Parse(manifest);
    }
}
