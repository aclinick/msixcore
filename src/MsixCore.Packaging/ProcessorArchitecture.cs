namespace MsixCore.Packaging;

/// <summary>
/// Processor architecture declared by an MSIX package identity.
/// Mirrors the values of the Windows <c>APPX_PACKAGE_ARCHITECTURE</c>/<c>APPX_PACKAGE_ARCHITECTURE2</c>
/// enumerations, but is usable on any platform.
/// </summary>
public enum ProcessorArchitecture
{
    /// <summary>Neutral / architecture-independent package.</summary>
    Neutral = 0,

    /// <summary>32-bit x86.</summary>
    X86 = 1,

    /// <summary>64-bit x64 (AMD64).</summary>
    X64 = 2,

    /// <summary>32-bit ARM.</summary>
    Arm = 3,

    /// <summary>64-bit ARM.</summary>
    Arm64 = 4,

    /// <summary>x86 running under ARM64 emulation (arm64ec / x86 on arm).</summary>
    X86OnArm64 = 5,
}
