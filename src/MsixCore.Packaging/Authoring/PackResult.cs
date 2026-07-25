namespace MsixCore.Packaging.Authoring;

/// <summary>Describes a successfully authored MSIX package.</summary>
public sealed record PackResult
{
    /// <summary>The absolute path of the package that was written.</summary>
    public required string OutputPath { get; init; }

    /// <summary>The package identity read back from the authored manifest.</summary>
    public required PackageIdentity Identity { get; init; }

    /// <summary>The number of payload files covered by the generated block map.</summary>
    public required int FileCount { get; init; }

    /// <summary>The total uncompressed size of the block-mapped payload files.</summary>
    public required long TotalSize { get; init; }
}
