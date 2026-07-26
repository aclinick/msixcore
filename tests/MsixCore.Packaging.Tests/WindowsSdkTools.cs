using System.Runtime.InteropServices;

namespace MsixCore.Packaging.Tests;

/// <summary>Locates Windows SDK tools that some tests compare our output against.</summary>
internal static class WindowsSdkTools
{
    /// <summary>
    /// Returns a makeappx.exe the current host can execute, or <see langword="null"/> when the
    /// Windows SDK is not installed (or this is not Windows), in which case the caller should skip.
    /// </summary>
    /// <remarks>
    /// The SDK ships makeappx.exe once per architecture. Preferring a fixed architecture works only
    /// on the machine the preference was written on: an arm64 binary on an x64 host fails to start
    /// with "not a valid application for this OS platform", which reads as a test failure rather
    /// than as an absent tool.
    /// </remarks>
    public static string? FindMakeAppx()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string kitsBin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits",
            "10",
            "bin");
        if (!Directory.Exists(kitsBin))
        {
            return null;
        }

        // Descending ordinal order puts the newest SDK version folder first within each architecture.
        string[] candidates = Directory.EnumerateFiles(kitsBin, "makeappx.exe", SearchOption.AllDirectories)
            .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string architecture in ExecutableArchitectures())
        {
            string segment = $"{Path.DirectorySeparatorChar}{architecture}{Path.DirectorySeparatorChar}";
            string? match = Array.Find(
                candidates,
                path => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>The SDK bin architectures this host can execute, best first.</summary>
    /// <remarks>
    /// Windows on ARM64 emulates x86 on every version, but x64 emulation arrived in Windows 11
    /// (build 22000). Listing x64 on Windows 10 ARM64 would recreate the very failure this ranking
    /// exists to avoid.
    /// </remarks>
    internal static string[] ExecutableArchitectures() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) =>
            ["arm64", "x64", "x86"],
        Architecture.Arm64 => ["arm64", "x86"],
        Architecture.X64 => ["x64", "x86"],
        Architecture.X86 => ["x86"],
        Architecture.Arm => ["arm", "x86"],
        _ => ["x64", "x86"],
    };
}
