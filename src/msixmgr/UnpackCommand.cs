using System.Globalization;
using System.Text.Json;
using MsixCore.Deployment;
using MsixCore.Packaging;

namespace MsixMgr;

/// <summary>
/// <c>unpack</c> verb: extracts a package's payload to a directory as a loose layout without
/// installing it. Cross-platform (no OS integration), so it works on Linux CI the same as Windows.
/// </summary>
internal static class UnpackCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out string? path, out string? destination, out bool json, out string? parseError))
        {
            error.WriteLine($"msixmgr unpack: {parseError}");
            error.WriteLine("Usage: msixmgr unpack <package-file-or-directory> -Destination <dir> [--json]");
            return 2;
        }

        try
        {
            using MsixPackage package = PackageOpener.Open(path!);
            PackageExtractor.Extract(package.Opc, destination!);
            int count = package.Opc.PartNames.Count;

            if (json)
            {
                var report = new UnpackReport
                {
                    Destination = Path.GetFullPath(destination!),
                    ExtractedPartCount = count,
                };
                output.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.UnpackReport));
            }
            else
            {
                string files = count.ToString(CultureInfo.InvariantCulture);
                output.WriteLine($"Extracted {files} parts to {Path.GetFullPath(destination!)}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine($"msixmgr unpack: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out string? path,
        out string? destination,
        out bool json,
        out string? error)
    {
        path = null;
        destination = null;
        json = false;
        error = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg is "--json")
            {
                json = true;
            }
            else if (arg is "-Destination" or "-destination" or "--destination" or "-d")
            {
                if (i + 1 >= args.Count)
                {
                    error = $"option '{arg}' requires a directory argument.";
                    return false;
                }

                destination = args[++i];
            }
            else if (arg.StartsWith('-'))
            {
                error = $"unknown option '{arg}'.";
                return false;
            }
            else if (path is null)
            {
                path = arg;
            }
            else
            {
                error = "expected a single package path.";
                return false;
            }
        }

        if (path is null)
        {
            error = "a package path is required.";
            return false;
        }

        if (string.IsNullOrEmpty(destination))
        {
            error = "a destination directory is required (-Destination <dir>).";
            return false;
        }

        return true;
    }
}
