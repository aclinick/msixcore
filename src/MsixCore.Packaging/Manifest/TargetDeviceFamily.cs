namespace MsixCore.Packaging.Manifest;

/// <summary>
/// A <c>TargetDeviceFamily</c> dependency declared in the manifest, constraining which OS families
/// and versions the package targets (e.g. <c>Windows.Desktop</c>, <c>MSIXCore.Desktop</c>).
/// </summary>
public sealed record TargetDeviceFamily
{
    /// <summary>The device family name (e.g. <c>Windows.Universal</c>, <c>Windows.Desktop</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The minimum required OS version.</summary>
    public required Version MinVersion { get; init; }

    /// <summary>The maximum OS version the package was tested against.</summary>
    public required Version MaxVersionTested { get; init; }
}
