using System.Runtime.InteropServices;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Locates makeappx.exe from PATH or installed Windows SDK folders.</summary>
public sealed class MakeAppxLocator
{
    /// <summary>Returns the best available makeappx.exe path, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The Windows SDK ships makeappx.exe once per architecture. Picking a fixed architecture works
    /// only on the machine it was written on: an arm64 binary cannot start on an x64 host, and the
    /// failure surfaces as a confusing "not a valid application for this OS platform"
    /// <see cref="System.ComponentModel.Win32Exception"/> rather than as "tool not found".
    /// Candidates are therefore ranked by what the host can actually execute.
    /// </remarks>
    public static string? Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? fromPath = FindOnPath();
        if (fromPath is not null)
        {
            return fromPath;
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

    private static string? FindOnPath()
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory.Trim(), "makeappx.exe");
            if (File.Exists(candidate) && CanExecute(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether this host can start <paramref name="path"/>, by reading the PE header's
    /// machine type.
    /// </summary>
    /// <remarks>
    /// A PATH entry is not architecture-tagged the way the SDK's directory layout is, so without
    /// this check a stray arm64 makeappx.exe on PATH would reintroduce the "not a valid application
    /// for this OS platform" failure that ranking the SDK directories avoids. A file we cannot read
    /// or do not understand is treated as runnable so that an unexpected PE variant degrades to the
    /// previous behaviour rather than silently hiding the tool.
    /// </remarks>
    private static bool CanExecute(string path)
    {
        string[] runnable = ExecutableArchitectures();
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            {
                return true;
            }

            stream.Position = 0x3C;
            uint peOffset = reader.ReadUInt32();
            if (peOffset + 6 > stream.Length)
            {
                return true;
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return true;
            }

            // IMAGE_FILE_HEADER.Machine immediately follows the PE signature.
            string? architecture = reader.ReadUInt16() switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0x01C4 or 0x01C0 => "arm",
                0xAA64 => "arm64",
                _ => null,
            };

            return architecture is null || Array.IndexOf(runnable, architecture) >= 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
