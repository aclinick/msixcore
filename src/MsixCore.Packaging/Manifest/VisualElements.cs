namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:VisualElements</c> for an application: its user-facing name, description, logos, and
/// tile background color.
/// </summary>
public sealed record VisualElements
{
    /// <summary>The application display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The application description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The package-relative path to the 150x150 (medium tile) logo, if declared.</summary>
    public string? Square150x150Logo { get; init; }

    /// <summary>The package-relative path to the 44x44 (app list) logo, if declared.</summary>
    public string? Square44x44Logo { get; init; }

    /// <summary>The tile background color (e.g. <c>#0078D7</c> or <c>transparent</c>), if declared.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Whether the app is shown in the app list (<c>AppListEntry</c> not set to <c>none</c>).</summary>
    public bool AppListEntry { get; init; } = true;
}
