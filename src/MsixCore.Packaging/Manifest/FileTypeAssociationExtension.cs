namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:FileTypeAssociation</c> child of a <c>windows.fileTypeAssociation</c> extension:
/// the file extensions the app opens.
/// </summary>
public sealed record FileTypeAssociationExtension : ExtensionPayload
{
    /// <summary>
    /// The association name, unique within the package. The schema restricts it to
    /// <c>[-_.a-z0-9]+</c> — notably lower-case only.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The localizable display name shown for the association, if declared.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The package-relative logo shown for the associated files, if declared.</summary>
    public string? Logo { get; init; }

    /// <summary>The tooltip shown for the associated files, if declared.</summary>
    public string? InfoTip { get; init; }

    /// <summary>
    /// The file types claimed. The schema requires <c>SupportedFileTypes</c> with at least one
    /// <c>FileType</c>, but a manifest that omits them still parses here and yields an empty list.
    /// </summary>
    public IReadOnlyList<SupportedFileType> FileTypes { get; init; } = [];
}

/// <summary>A single <c>uap:FileType</c> inside a file type association.</summary>
public sealed record SupportedFileType
{
    /// <summary>
    /// The file extension, including the leading dot (the schema pattern <c>\.[^.\\]+</c> requires
    /// it). Preserved exactly as written rather than normalised, so tooling can report a manifest
    /// that declares <c>.TXT</c> distinctly from <c>.txt</c>.
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>The MIME content type registered for the extension, if declared.</summary>
    public string? ContentType { get; init; }
}
