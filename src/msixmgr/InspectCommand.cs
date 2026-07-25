using System.Globalization;
using System.Text.Json;
using MsixCore.Packaging;

namespace MsixMgr;

/// <summary>
/// <c>inspect</c> verb: prints package identity and metadata as human-readable text or JSON. Works
/// on container files and loose directories, and runs cross-platform (no OS integration required).
/// </summary>
internal static class InspectCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out string? path, out bool json, out string? parseError))
        {
            error.WriteLine($"msixmgr inspect: {parseError}");
            error.WriteLine("Usage: msixmgr inspect <package-file-or-directory> [--json]");
            return 2;
        }

        try
        {
            using MsixPackage package = PackageOpener.Open(path!);
            InspectionReport report = Build(package);
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(report, ReportJson.Options));
            }
            else
            {
                WriteText(report, output);
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine($"msixmgr inspect: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParse(IReadOnlyList<string> args, out string? path, out bool json, out string? error)
    {
        path = null;
        json = false;
        error = null;
        foreach (string arg in args)
        {
            if (arg is "--json")
            {
                json = true;
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

        return true;
    }

    private static InspectionReport Build(MsixPackage package)
    {
        int? blockMapFiles = null;
        string? hashMethod = null;
        try
        {
            blockMapFiles = package.BlockMap.Files.Count;
            hashMethod = package.BlockMap.HashMethod.ToString();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // A missing/invalid block map is reported by `validate`; inspect should still show identity.
        }

        return new InspectionReport
        {
            Name = package.Identity.Name,
            PackageFullName = package.Identity.PackageFullName,
            PackageFamilyName = package.Identity.PackageFamilyName,
            Version = package.Identity.Version.ToString(),
            Architecture = PackageIdentity.ArchitectureMoniker(package.Identity.Architecture),
            DisplayName = package.DisplayName,
            PublisherDisplayName = package.PublisherDisplayName,
            Capabilities = package.Capabilities,
            IsSigned = package.IsSigned,
            BlockMapFileCount = blockMapFiles,
            BlockMapHashMethod = hashMethod,
        };
    }

    private static void WriteText(InspectionReport r, TextWriter o)
    {
        o.WriteLine($"Name            : {r.Name}");
        o.WriteLine($"Full name       : {r.PackageFullName}");
        o.WriteLine($"Family name     : {r.PackageFamilyName}");
        o.WriteLine($"Version         : {r.Version}");
        o.WriteLine($"Architecture    : {r.Architecture}");
        o.WriteLine($"Display name    : {r.DisplayName}");
        o.WriteLine($"Publisher       : {r.PublisherDisplayName}");
        o.WriteLine($"Signed          : {r.IsSigned}");
        string capabilities = r.Capabilities.Count == 0 ? "(none)" : string.Join(", ", r.Capabilities);
        o.WriteLine($"Capabilities    : {capabilities}");
        if (r.BlockMapFileCount is int count)
        {
            string files = count.ToString(CultureInfo.InvariantCulture);
            o.WriteLine($"Block map       : {files} files ({r.BlockMapHashMethod})");
        }
        else
        {
            o.WriteLine("Block map       : (none)");
        }
    }
}
