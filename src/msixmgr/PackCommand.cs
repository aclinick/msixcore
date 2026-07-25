using System.Globalization;
using System.IO.Compression;
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
            out bool compress,
            out bool json,
            out string? parseError))
        {
            CliContract.WriteError(
                output,
                error,
                json || CliContract.HasJsonFlag(args),
                "msixmgr pack",
                parseError!,
                "Usage: msixmgr pack <sourceDir> -o|--output <file.msix> [--compress] [--overwrite] [--json]");
            return CliContract.ExitCodes.Usage;
        }

        try
        {
            PackResult result = MsixPackageBuilder.Build(
                sourceDirectory!,
                outputPath!,
                new PackOptions
                {
                    Overwrite = overwrite,
                    CompressionLevel = compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression,
                });
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    CreateReport(result),
                    ReportJsonContext.Default.PackReport));
            }
            else
            {
                string files = result.FileCount.ToString(CultureInfo.InvariantCulture);
                string bytes = result.TotalSize.ToString(CultureInfo.InvariantCulture);
                output.WriteLine($"Packed {files} files ({bytes} bytes) to {result.OutputPath}");
                output.WriteLine($"Identity: {result.Identity.PackageFullName}");
            }

            return CliContract.ExitCodes.Success;
        }
        catch (Exception ex) when (
            CliContract.IsOperationalException(ex) || ex is ArgumentException)
        {
            CliContract.WriteError(output, error, json, "msixmgr pack", ex.Message, null, CliContract.ErrorCode(ex));
            return CliContract.ExitCodes.OperationalError;
        }
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out string? sourceDirectory,
        out string? outputPath,
        out bool overwrite,
        out bool compress,
        out bool json,
        out string? error)
    {
        sourceDirectory = null;
        outputPath = null;
        overwrite = false;
        compress = false;
        json = false;
        error = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg is "--overwrite")
            {
                overwrite = true;
            }
            else if (arg is "--compress")
            {
                compress = true;
            }
            else if (arg is "--json")
            {
                json = true;
            }
            else if (arg is "-o" or "--output")
            {
                if (outputPath is not null)
                {
                    error = "the output option may be specified only once.";
                    return false;
                }

                if (!CliContract.TryReadOptionValue(args, ref i, arg, "a file argument", out outputPath, out error))
                {
                    return false;
                }
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
            Compression = result.CompressionLevel == CompressionLevel.NoCompression ? "Stored" : "Normal",
        };
    }
}
