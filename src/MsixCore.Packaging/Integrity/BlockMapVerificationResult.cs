namespace MsixCore.Packaging.Integrity;

/// <summary>The outcome of verifying one block-mapped file against the package contents.</summary>
public sealed record BlockMapFileResult
{
    /// <summary>The package-relative file name (forward slashes).</summary>
    public required string Name { get; init; }

    /// <summary>Whether the file's size and every block hash matched the block map.</summary>
    public required bool IsValid { get; init; }

    /// <summary>A human-readable description of the first mismatch, when <see cref="IsValid"/> is <see langword="false"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>The overall result of a block-map verification pass.</summary>
public sealed record BlockMapVerificationResult
{
    /// <summary>Whether every file matched and package/block-map coverage was consistent.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Per-file verification results, in block-map order.</summary>
    public required IReadOnlyList<BlockMapFileResult> Files { get; init; }

    /// <summary>
    /// Coverage problems not tied to a single file: package payload parts absent from the block map,
    /// or block-map files absent from the package.
    /// </summary>
    public required IReadOnlyList<string> CoverageErrors { get; init; }
}
