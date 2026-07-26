using System.Globalization;
using System.Runtime.InteropServices;

namespace MsixCore.Packaging.Bundles;

/// <summary>
/// The device context a bundle is being resolved for: which processor architecture must run, which
/// languages the user prefers, and which display scale and DirectX feature level apply.
/// </summary>
/// <remarks>
/// A qualifier left unset means "do not filter on this". That is why <see cref="Scale"/> and
/// <see cref="DXFeatureLevel"/> are nullable and <see cref="Languages"/> may be empty: an empty
/// value selects every package rather than none, which keeps a partially-specified target from
/// silently discarding payload.
/// </remarks>
public sealed record BundleTarget
{
    /// <summary>The processor architecture that must be able to execute the selected app package.</summary>
    public required ProcessorArchitecture Architecture { get; init; }

    /// <summary>
    /// The preferred languages in descending order of preference (e.g. <c>["fr-FR", "en-US"]</c>).
    /// Empty means language is not used to filter.
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>
    /// The display scale as a percentage (e.g. <c>200</c>), or <see langword="null"/> to not filter
    /// on scale.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    /// The DirectX feature level (e.g. <c>DX11</c>), or <see langword="null"/> to not filter on it.
    /// </summary>
    public string? DXFeatureLevel { get; init; }

    /// <summary>
    /// Builds a target describing the machine this process is running on: the current device
    /// architecture and the current UI culture followed by its parent cultures.
    /// </summary>
    /// <returns>A target for the current device.</returns>
    /// <remarks>
    /// Scale and DirectX feature level are left unset. Neither is discoverable from a
    /// cross-platform runtime API, and guessing them would silently drop resource packages.
    /// </remarks>
    public static BundleTarget Current() => new()
    {
        Architecture = CurrentArchitecture(),
        Languages = CurrentLanguages(),
    };

    /// <summary>Maps the current device architecture onto an MSIX package architecture.</summary>
    /// <returns>The current architecture, or <see cref="ProcessorArchitecture.Unknown"/> if unmapped.</returns>
    /// <remarks>
    /// Uses <see cref="RuntimeInformation.OSArchitecture"/>, not <c>ProcessArchitecture</c>. What can
    /// be installed is a property of the machine, not of the bitness this process happens to be
    /// running at: a 32-bit tool on an x64 machine must still resolve x64 packages, and a tool
    /// running under ARM64 emulation must not reject an ARM64-only bundle.
    /// </remarks>
    internal static ProcessorArchitecture CurrentArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86 => ProcessorArchitecture.X86,
        System.Runtime.InteropServices.Architecture.X64 => ProcessorArchitecture.X64,
        System.Runtime.InteropServices.Architecture.Arm => ProcessorArchitecture.Arm,
        System.Runtime.InteropServices.Architecture.Arm64 => ProcessorArchitecture.Arm64,
        _ => ProcessorArchitecture.Unknown,
    };

    private static List<string> CurrentLanguages()
    {
        var languages = new List<string>();
        for (CultureInfo culture = CultureInfo.CurrentUICulture;
             !string.IsNullOrEmpty(culture.Name);
             culture = culture.Parent)
        {
            languages.Add(culture.Name);
        }

        return languages;
    }
}
