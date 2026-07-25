namespace MsixCore.CorpusRoundtrip;

/// <summary>Locates makeappx.exe from PATH or installed Windows SDK folders.</summary>
public sealed class MakeAppxLocator
{
    /// <summary>Returns the best available makeappx.exe path, or <see langword="null"/>.</summary>
    public static string? Find()
    {
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

        return Directory.EnumerateFiles(kitsBin, "makeappx.exe", SearchOption.AllDirectories)
            .Where(static path => path.Contains(
                string.Concat(Path.DirectorySeparatorChar, "x64", Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

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
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
