using System.IO.Compression;

namespace MsixCore.Packaging.Authoring;

/// <summary>Controls how an unsigned MSIX package is written.</summary>
public sealed record PackOptions
{
    /// <summary>Whether an existing output file may be replaced.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// The MSIX payload compression level. <see cref="CompressionLevel.NoCompression"/> preserves the
    /// deterministic Stored output; <see cref="CompressionLevel.Optimal"/> uses MakeAppx-compatible,
    /// independently restartable raw-DEFLATE blocks.
    /// </summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.NoCompression;
}
