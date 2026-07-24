namespace MsixCore.Packaging.Integrity;

/// <summary>The cryptographic hash algorithm declared by an <c>AppxBlockMap.xml</c>.</summary>
public enum BlockMapHashMethod
{
    /// <summary>SHA-256 (<c>http://www.w3.org/2001/04/xmlenc#sha256</c>). The MSIX default.</summary>
    Sha256 = 0,

    /// <summary>SHA-384 (<c>http://www.w3.org/2001/04/xmldsig-more#sha384</c>).</summary>
    Sha384 = 1,

    /// <summary>SHA-512 (<c>http://www.w3.org/2001/04/xmlenc#sha512</c>).</summary>
    Sha512 = 2,
}

/// <summary>
/// A single block within a block-mapped file. The <see cref="Hash"/> is over the <em>uncompressed</em>
/// block content (up to 64&#160;KiB); <see cref="CompressedSize"/> is the block's stored size and is
/// present only when the file is compressed.
/// </summary>
public sealed record BlockMapBlock
{
    /// <summary>The base64-encoded hash of the uncompressed block, as declared in the block map.</summary>
    public required string Hash { get; init; }

    /// <summary>The stored (compressed) size of the block in bytes, if the file is compressed.</summary>
    public long? CompressedSize { get; init; }
}

/// <summary>A file entry in the block map: its logical name, uncompressed size, and ordered blocks.</summary>
public sealed record BlockMapFile
{
    /// <summary>
    /// The package-relative file name using forward slashes (normalized from the block map's native
    /// backslash form) so it can be looked up directly against OPC part names.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The total uncompressed size of the file in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The ordered blocks that make up the file.</summary>
    public required IReadOnlyList<BlockMapBlock> Blocks { get; init; }
}

/// <summary>The strongly-typed contents of an <c>AppxBlockMap.xml</c>.</summary>
public sealed record BlockMap
{
    /// <summary>The uncompressed block size (64&#160;KiB) every block but the last of a file fills.</summary>
    public const int BlockSize = 64 * 1024;

    /// <summary>The hash algorithm used for all block hashes.</summary>
    public required BlockMapHashMethod HashMethod { get; init; }

    /// <summary>The block-mapped files, keyed in document order.</summary>
    public required IReadOnlyList<BlockMapFile> Files { get; init; }
}
