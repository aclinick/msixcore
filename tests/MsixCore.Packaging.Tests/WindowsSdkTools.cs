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

    /// <summary>The SDK bin architecture this host runs, or empty on an unsupported host.</summary>
    /// <remarks>
    /// The tools always run native. arm64 and x64 are the supported hosts, and neither falls back
    /// to the other: an arm64 host runs arm64 binaries even though Windows 11 could emulate x64.
    /// Emulation is deliberately not used, so there is no version gate here. An unsupported host
    /// runs nothing, which callers read as "tool absent".
    /// </remarks>
    internal static string[] ExecutableArchitectures() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => ["arm64"],
        Architecture.X64 => ["x64"],
        _ => [],
    };
}
