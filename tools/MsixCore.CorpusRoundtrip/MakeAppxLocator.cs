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
        if (!OperatingSystem.IsWindows() || ExecutableArchitectures().Length == 0)
        {
            // An unsupported host can run nothing, so the tool is absent by definition. Checking
            // here rather than only per-candidate keeps CanExecute's fail-open policy — which
            // accepts a file it cannot parse — from letting a PATH candidate through.
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
        if (runnable.Length == 0)
        {
            return false;
        }

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
