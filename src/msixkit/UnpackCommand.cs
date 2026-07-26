using System.Globalization;
using System.Text.Json;
using MsixCore.PackageStore;
using MsixCore.Packaging;

namespace MsixKit;

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
            CliContract.WriteError(
                output,
                error,
                json || CliContract.HasJsonFlag(args),
                "msixkit unpack",
                parseError!,
                "Usage: msixkit unpack <package-file-or-directory> -Destination <dir> [--json]");
            return CliContract.ExitCodes.Usage;
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

            return CliContract.ExitCodes.Success;
        }
        catch (Exception ex) when (CliContract.IsOperationalException(ex))
        {
            CliContract.WriteError(output, error, json, "msixkit unpack", ex.Message, null, CliContract.ErrorCode(ex));
            return CliContract.ExitCodes.OperationalError;
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
                if (!CliContract.TryReadOptionValue(args, ref i, arg, "a directory argument", out destination, out error))
                {
                    return false;
                }
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
