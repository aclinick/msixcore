namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>desktop:FullTrustProcess</c> child of a <c>windows.fullTrustProcess</c> extension: a
/// full-trust executable a UWP app may launch.
/// </summary>
/// <remarks>
/// The executable itself lives on the <c>Executable</c> attribute of the enclosing
/// <see cref="AppExtension"/>, not on this element, which carries only the parameter groups. An
/// extension with no <c>FullTrustProcess</c> child is valid and common, in which case
/// <see cref="AppExtension.Payload"/> is <see langword="null"/>.
/// </remarks>
public sealed record FullTrustProcessExtension : ExtensionPayload
{
    /// <summary>The parameter groups the app may select between when launching the process.</summary>
    public IReadOnlyList<ParameterGroup> ParameterGroups { get; init; } = [];
}

/// <summary>A named set of command-line arguments for a full-trust process.</summary>
public sealed record ParameterGroup
{
    /// <summary>The id the app passes to select this group.</summary>
    public required string GroupId { get; init; }

    /// <summary>The command line passed to the executable for this group.</summary>
    public required string Parameters { get; init; }
}
