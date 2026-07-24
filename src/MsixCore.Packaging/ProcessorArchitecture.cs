namespace MsixCore.Packaging;

/// <summary>
/// Processor architecture declared by an MSIX package identity.
/// The numeric values match the Windows <c>APPX_PACKAGE_ARCHITECTURE</c>/<c>APPX_PACKAGE_ARCHITECTURE2</c>
/// enumerations so telemetry, serialization, and any future interop stay faithful, but the type is
/// usable on any platform.
/// </summary>
public enum ProcessorArchitecture
{
    /// <summary>32-bit x86.</summary>
    X86 = 0,

    /// <summary>32-bit ARM.</summary>
    Arm = 5,

    /// <summary>64-bit x64 (AMD64).</summary>
    X64 = 9,

    /// <summary>Neutral / architecture-independent package.</summary>
    Neutral = 11,

    /// <summary>64-bit ARM.</summary>
    Arm64 = 12,

    /// <summary>x86 running under ARM64 emulation (x86 on ARM64).</summary>
    X86OnArm64 = 14,

    /// <summary>Unknown or unspecified architecture.</summary>
    Unknown = 0xFFFF,
}
