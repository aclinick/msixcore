using System.Globalization;
using System.Text.Json;
using MsixCore.Packaging;
using MsixCore.Packaging.Authoring;
using MsixCore.Packaging.Manifest;

namespace MsixKit;

/// <summary><c>bundle</c> verb: builds an unsigned bundle from MSIX/APPX packages.</summary>
internal static class BundleCommand
{
    internal static Func<IEnumerable<string>, string, BundleOptions?, BundleResult> BuildBundle { get; set; } = MsixBundleBuilder.Build;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParse(
            args,
            out List<string> packagePaths,
            out string? outputPath,
            out Version? version,
            out bool overwrite,
            out bool json,
            out string? parseError))
        {
            CliContract.WriteError(
                output,
                error,
                json || CliContract.HasJsonFlag(args),
                "msixkit bundle",
                parseError!,
                "Usage: msixkit bundle <package.msix>... -o|--output <file.msixbundle> "
                    + "[--version <a.b.c.d>] [--overwrite] [--json]");
            return CliContract.ExitCodes.Usage;
        }

        try
        {
            BundleResult result = BuildBundle(
                packagePaths,
                outputPath!,
                new BundleOptions { Overwrite = overwrite, Version = version });
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    CreateReport(result),
                    ReportJsonContext.Default.BundleReport));
            }
            else
            {
                output.WriteLine(
                    $"Bundled {result.PackageCount.ToString(CultureInfo.InvariantCulture)} packages "
                    + $"({result.TotalSize.ToString(CultureInfo.InvariantCulture)} bytes) "
                    + $"to {result.OutputPath}");
                output.WriteLine($"Identity: {result.Identity.PackageFullName}");
            }

            return CliContract.ExitCodes.Success;
        }
        catch (Exception ex) when (
            CliContract.IsOperationalException(ex) || ex is ArgumentException or InvalidOperationException)
        {
            CliContract.WriteError(output, error, json, "msixkit bundle", ex.Message, null, CliContract.ErrorCode(ex));
            return CliContract.ExitCodes.OperationalError;
        }
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out List<string> packagePaths,
        out string? outputPath,
        out Version? version,
        out bool overwrite,
        out bool json,
        out string? error)
    {
        packagePaths = [];
        outputPath = null;
        version = null;
        overwrite = false;
        json = false;
        error = null;
        bool versionSpecified = false;

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
                if (outputPath is not null)
                {
                    error = "the output option may be specified only once.";
                    return false;
                }

                if (!CliContract.TryReadOptionValue(args, ref i, arg, "an argument", out outputPath, out error))
                {
                    return false;
                }
            }
            else if (arg is "--version")
            {
                if (versionSpecified)
                {
                    error = "the version option may be specified only once.";
                    return false;
                }

                if (!CliContract.TryReadOptionValue(args, ref i, arg, "an argument", out string? versionText, out error))
                {
                    return false;
                }

                versionSpecified = true;

                if (!Version.TryParse(versionText, out version)
                    || version.Major < 0
                    || version.Minor < 0
                    || version.Build < 0
                    || version.Revision < 0
                    || version.Major > ushort.MaxValue
                    || version.Minor > ushort.MaxValue
                    || version.Build > ushort.MaxValue
                    || version.Revision > ushort.MaxValue)
                {
                    error = $"option '{arg}' requires a four-part version (for example, 1.2.3.4).";
                    return false;
                }
            }
            else if (arg.StartsWith('-'))
            {
                error = $"unknown option '{arg}'.";
                return false;
            }
            else
            {
                packagePaths.Add(arg);
            }
        }

        if (packagePaths.Count == 0)
        {
            error = "at least one child package is required.";
            return false;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            error = "an output file is required (-o <file.msixbundle>).";
            return false;
        }

        return true;
    }

    private static BundleReport CreateReport(BundleResult result) => new()
    {
        OutputPath = result.OutputPath,
        Name = result.Identity.Name,
        PackageFullName = result.Identity.PackageFullName,
        PackageFamilyName = result.Identity.PackageFamilyName,
        Version = result.Identity.Version.ToString(),
        PackageCount = result.PackageCount,
        TotalSize = result.TotalSize,
        IsSigned = false,
        Packages = result.Packages.Select(CreatePackageReport).ToArray(),
    };

    private static BundlePackageReport CreatePackageReport(BundlePackageEntry package) => new()
    {
        FileName = package.FileName,
        Type = package.Type == BundlePackageType.Application ? "application" : "resource",
        Version = package.Version.ToString(),
        Architecture = package.Type == BundlePackageType.Application
            ? PackageIdentity.ArchitectureMoniker(package.Architecture)
            : null,
        ResourceId = string.IsNullOrEmpty(package.ResourceId) ? null : package.ResourceId,
        Offset = package.Offset,
        Size = package.Size,
    };
}
