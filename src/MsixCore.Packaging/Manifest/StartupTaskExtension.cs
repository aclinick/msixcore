namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>StartupTask</c> child of a <c>windows.startupTask</c> extension: an executable run when
/// the user signs in.
/// </summary>
/// <remarks>
/// Both the <c>desktop:StartupTask</c> and <c>uap5:StartupTask</c> forms collapse onto this type;
/// they declare the same three attributes and differ only in later optional additions.
/// </remarks>
public sealed record StartupTaskExtension : ExtensionPayload
{
    /// <summary>The task id the app passes to the startup task APIs to query or toggle the task.</summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Whether the task starts enabled, or <see langword="null"/> when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// Nullable rather than defaulted: the schema declares no default for <c>Enabled</c>, and
    /// inventing one here would report a manifest fact that the manifest does not state. The
    /// distinction matters to tooling that diffs or explains manifests.
    /// </remarks>
    public bool? IsEnabled { get; init; }

    /// <summary>The name shown to the user in the startup apps list, if declared.</summary>
    public string? DisplayName { get; init; }
}
