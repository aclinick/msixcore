namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>desktop7:Shortcut</c> child of a <c>windows.shortcut</c> extension: a Start menu shortcut
/// the package creates.
/// </summary>
public sealed record ShortcutExtension : ExtensionPayload
{
    /// <summary>The package-relative path of the shortcut (<c>.lnk</c>) file to create.</summary>
    public required string File { get; init; }

    /// <summary>The package-relative icon shown for the shortcut.</summary>
    public required string Icon { get; init; }

    /// <summary>The command line passed to the shortcut target, if declared.</summary>
    public string? Arguments { get; init; }

    /// <summary>The description shown for the shortcut, if declared.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the shortcut is pinned to the Start menu; <see langword="null"/> when unstated.</summary>
    public bool? PinToStartMenu { get; init; }
}
