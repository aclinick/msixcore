using System.IO.Compression;

namespace MsixCore.Packaging.Authoring;

/// <summary>Controls how an unsigned MSIX package is written.</summary>
public sealed record PackOptions
{
    /// <summary>Whether an existing output file may be replaced.</summary>
    public bool Overwrite { get; init; }

    /// <summary>The ZIP compression level used for payload and generated footprint entries.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
}
