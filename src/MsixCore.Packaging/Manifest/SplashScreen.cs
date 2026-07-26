namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:SplashScreen</c> child of <see cref="VisualElements"/>.
/// </summary>
public sealed record SplashScreen
{
    /// <summary>The package-relative path to the splash screen image. Required by the schema.</summary>
    public required string Image { get; init; }

    /// <summary>The splash screen background color, if declared.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>
    /// The <c>uap5:Optional</c> flag, or <see langword="null"/> when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// The schema declares no default, so "unstated" is kept distinct from "stated false".
    /// </remarks>
    public bool? IsOptional { get; init; }
}
