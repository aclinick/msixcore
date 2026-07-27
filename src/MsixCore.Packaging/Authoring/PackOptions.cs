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

    /// <summary>
    /// The maximum number of blocks compressed concurrently. <c>0</c> (the default) uses
    /// <see cref="Environment.ProcessorCount"/>; <c>1</c> forces the sequential path. Output is
    /// byte-identical at every setting because MSIX blocks are compressed independently.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; }
}
