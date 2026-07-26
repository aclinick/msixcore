namespace MsixCore.Packaging.Manifest;

/// <summary>
/// An <c>&lt;Application&gt;</c> declared in the manifest.
/// </summary>
public sealed record ManifestApplication
{
    /// <summary>The application id (unique within the package).</summary>
    public required string Id { get; init; }

    /// <summary>The package-relative executable path, if the app is a classic/full-trust app.</summary>
    public string? Executable { get; init; }

    /// <summary>The entry point (a runtime class name, or <c>Windows.FullTrustApplication</c>), if declared.</summary>
    public string? EntryPoint { get; init; }

    /// <summary>The application protocol/URI scheme handled, if declared via the startup extension.</summary>
    public VisualElements VisualElements { get; init; } = new();

    /// <summary>
    /// The OS integration points this application declares, e.g. file type associations and
    /// protocol handlers. Empty when the application declares no <c>&lt;Extensions&gt;</c>.
    /// </summary>
    public IReadOnlyList<AppExtension> Extensions { get; init; } = [];
}
