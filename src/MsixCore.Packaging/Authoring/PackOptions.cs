using System.IO.Compression;

namespace MsixCore.Packaging.Authoring;

/// <summary>Controls how an unsigned MSIX package is written.</summary>
public sealed record PackOptions
{
    /// <summary>Whether an existing output file may be replaced.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// The ZIP compression level. Only <see cref="CompressionLevel.NoCompression"/> is currently
    /// supported because MSIX compression must operate independently on each 64 KiB block.
    /// </summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.NoCompression;
}
