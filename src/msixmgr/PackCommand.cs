using System.Globalization;
using System.Text.Json;
using MsixCore.Packaging;
using MsixCore.Packaging.Authoring;

namespace MsixMgr;

/// <summary><c>pack</c> verb: builds an unsigned MSIX package from a source directory.</summary>
internal static class PackCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParse(
            args,
            out string? sourceDirectory,
            out string? outputPath,
            out bool overwrite,
            out bool json,
            out string? parseError))
        {
            error.WriteLine($"msixmgr pack: {parseError}");
            error.WriteLine("Usage: msixmgr pack <sourceDir> -o|--output <file.msix> [--overwrite] [--json]");
            return 2;
        }

        try
        {
            PackResult result = MsixPackageBuilder.Build(
                sourceDirectory!,
                outputPath!,
                new PackOptions { Overwrite = overwrite });
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(CreateReport(result), ReportJson.Options));
            }
            else
            {
                string files = result.FileCount.ToString(CultureInfo.InvariantCulture);
                string bytes = result.TotalSize.ToString(CultureInfo.InvariantCulture);
                output.WriteLine($"Packed {files} files ({bytes} bytes) to {result.OutputPath}");
                output.WriteLine($"Identity: {result.Identity.PackageFullName}");
            }

            return 0;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"msixmgr pack: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out string? sourceDirectory,
        out string? outputPath,
        out bool overwrite,
        out bool json,
        out string? error)
    {
        sourceDirectory = null;
        outputPath = null;
        overwrite = false;
        json = false;
        error = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg is "--overwrite")
            {
                overwrite = true;
            }
            else if (arg is "--json")
            {
                json = true;
            }
            else if (arg is "-o" or "--output")
            {
                if (i + 1 >= args.Count)
                {
                    error = $"option '{arg}' requires a file argument.";
                    return false;
                }

                if (outputPath is not null)
                {
                    error = "the output option may be specified only once.";
                    return false;
                }

                outputPath = args[++i];
            }
            else if (arg.StartsWith('-'))
            {
                error = $"unknown option '{arg}'.";
                return false;
            }
            else if (sourceDirectory is null)
            {
                sourceDirectory = arg;
            }
            else
            {
                error = "expected a single source directory.";
                return false;
            }
        }

        if (sourceDirectory is null)
        {
            error = "a source directory is required.";
            return false;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            error = "an output file is required (-o <file.msix>).";
            return false;
        }

        return true;
    }

    private static PackReport CreateReport(PackResult result)
    {
        PackageIdentity identity = result.Identity;
        return new PackReport
        {
            OutputPath = result.OutputPath,
            Name = identity.Name,
            PackageFullName = identity.PackageFullName,
            PackageFamilyName = identity.PackageFamilyName,
            Version = identity.Version.ToString(),
            Architecture = PackageIdentity.ArchitectureMoniker(identity.Architecture),
            FileCount = result.FileCount,
            TotalSize = result.TotalSize,
            IsSigned = false,
        };
    }
}
