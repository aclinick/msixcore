namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:LockScreen</c> child of <see cref="VisualElements"/>.
/// </summary>
public sealed record LockScreen
{
    /// <summary>The package-relative path to the badge logo. Required by the schema.</summary>
    public required string BadgeLogo { get; init; }

    /// <summary>
    /// The notification kind (<c>badge</c> or <c>badgeAndTileText</c>). Required by the schema.
    /// </summary>
    /// <remarks>
    /// Kept as the raw attribute text rather than an enum: the value set has grown across schema
    /// revisions before, and an unknown value should be reportable rather than fatal.
    /// </remarks>
    public required string Notification { get; init; }
}
