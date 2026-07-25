namespace MsixCore.Packaging.Authoring;

/// <summary>Controls how an unsigned MSIX bundle is written.</summary>
public sealed record BundleOptions
{
    /// <summary>Whether an existing output file may be replaced.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// The four-part bundle version. When omitted, the highest contained package version is used.
    /// </summary>
    public Version? Version { get; init; }
}
