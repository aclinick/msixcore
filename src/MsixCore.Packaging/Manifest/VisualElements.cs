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

    /// <summary>
    /// The <c>uap3:VisualElements/@VisualGroup</c> value, if declared: the group this app's tile is
    /// filed under in the app list.
    /// </summary>
    public string? VisualGroup { get; init; }

    /// <summary>
    /// The <c>uap:DefaultTile</c> declaration, if present. This carries the wide, large, and small
    /// tile logos, which are <em>not</em> attributes of <c>VisualElements</c> itself.
    /// </summary>
    public DefaultTile? DefaultTile { get; init; }

    /// <summary>The <c>uap:SplashScreen</c> declaration, if present.</summary>
    public SplashScreen? SplashScreen { get; init; }

    /// <summary>The <c>uap:LockScreen</c> declaration, if present.</summary>
    public LockScreen? LockScreen { get; init; }

    /// <summary>
    /// The declared initial rotation preferences, in document order, as the raw <c>Preference</c>
    /// values of <c>uap:InitialRotationPreference</c>/<c>uap:Rotation</c> (<c>portrait</c>,
    /// <c>landscape</c>, <c>portraitFlipped</c>, <c>landscapeFlipped</c>). Empty when unstated.
    /// </summary>
    public IReadOnlyList<string> InitialRotationPreferences { get; init; } = [];
}
