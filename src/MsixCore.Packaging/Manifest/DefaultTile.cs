namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:DefaultTile</c> child of <see cref="VisualElements"/>: the additional tile logos and
/// the short name used when the app is pinned to Start.
/// </summary>
/// <remarks>
/// Every attribute is optional in the schema, so every property here is nullable. The wide, large,
/// and small logos live on this element rather than on <c>VisualElements</c> itself.
/// </remarks>
public sealed record DefaultTile
{
    /// <summary>The package-relative path to the 310x150 (wide tile) logo, if declared.</summary>
    public string? Wide310x150Logo { get; init; }

    /// <summary>The package-relative path to the 310x310 (large tile) logo, if declared.</summary>
    public string? Square310x310Logo { get; init; }

    /// <summary>The package-relative path to the 71x71 (small tile) logo, if declared.</summary>
    public string? Square71x71Logo { get; init; }

    /// <summary>The short name shown on tiles when the display name does not fit, if declared.</summary>
    public string? ShortName { get; init; }

    /// <summary>
    /// The tile sizes that show the app name, from <c>uap:ShowNameOnTiles</c>/<c>uap:ShowOn</c>.
    /// Values are the raw <c>Tile</c> attribute text (<c>square150x150Logo</c>,
    /// <c>wide310x150Logo</c>, or <c>square310x310Logo</c>).
    /// </summary>
    /// <remarks>
    /// An empty list means <c>ShowNameOnTiles</c> was absent. The schema requires at least one
    /// <c>ShowOn</c> when the element is present, so an empty list is never ambiguous.
    /// </remarks>
    public IReadOnlyList<string> ShowNameOnTiles { get; init; } = [];
}
