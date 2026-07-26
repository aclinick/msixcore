using System.Globalization;
using System.Text.Json;
using MsixCore.Packaging;
using MsixCore.Packaging.Manifest;

namespace MsixKit;

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
            CliContract.WriteError(
                output,
                error,
                json || CliContract.HasJsonFlag(args),
                "msixkit inspect",
                parseError!,
                "Usage: msixkit inspect <package-file-or-directory> [--json]");
            return CliContract.ExitCodes.Usage;
        }

        try
        {
            using MsixPackage package = PackageOpener.Open(path!);
            InspectionReport report = Build(package);
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.InspectionReport));
            }
            else
            {
                WriteText(report, output);
            }

            return CliContract.ExitCodes.Success;
        }
        catch (Exception ex) when (CliContract.IsOperationalException(ex))
        {
            CliContract.WriteError(output, error, json, "msixkit inspect", ex.Message, null, CliContract.ErrorCode(ex));
            return CliContract.ExitCodes.OperationalError;
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
            DeclaredCapabilities = package.Manifest.DeclaredCapabilities
                .Select(static capability => new CapabilityReport
                {
                    Name = capability.Name,
                    Kind = CapabilityKindMoniker(capability.Kind),
                    Namespace = string.IsNullOrEmpty(capability.Namespace) ? null : capability.Namespace,
                })
                .ToList(),
            Dependencies = package.Manifest.PackageDependencies
                .Select(static dependency => new DependencyReport
                {
                    Kind = dependency.Kind switch
                    {
                        PackageDependencyKind.Framework => "framework",
                        PackageDependencyKind.MainPackage => "mainPackage",
                        PackageDependencyKind.HostRuntime => "hostRuntime",
                        _ => "unknown",
                    },
                    Name = dependency.Name,
                    Publisher = dependency.Publisher,
                    MinVersion = dependency.MinVersion?.ToString(),
                    MaxMajorVersionTested = dependency.MaxMajorVersionTested,
                    IsOptional = dependency.IsOptional,
                })
                .ToList(),
            Extensions = CollectExtensions(package.Manifest),
            IsSigned = package.IsSigned,
            BlockMapFileCount = blockMapFiles,
            BlockMapHashMethod = hashMethod,
        };
    }

    private static string CapabilityKindMoniker(CapabilityKind kind) => kind switch
    {
        CapabilityKind.General => "general",
        CapabilityKind.Device => "device",
        CapabilityKind.Restricted => "restricted",
        CapabilityKind.Windows => "windows",
        CapabilityKind.Custom => "custom",
        _ => "unknown",
    };

    /// <summary>
    /// Flattens the package-level and per-application extension containers into one reported list,
    /// tagging each entry with the declaring application so the two remain distinguishable.
    /// </summary>
    private static List<ExtensionReport> CollectExtensions(AppxManifest manifest)
    {
        var result = new List<ExtensionReport>();
        foreach (AppExtension extension in manifest.Extensions)
        {
            result.Add(ToReport(extension, applicationId: null));
        }

        foreach (ManifestApplication application in manifest.Applications)
        {
            foreach (AppExtension extension in application.Extensions)
            {
                result.Add(ToReport(extension, application.Id));
            }
        }

        return result;
    }

    private static ExtensionReport ToReport(AppExtension extension, string? applicationId) =>
        new()
        {
            ApplicationId = applicationId,
            Category = extension.Category,
            Executable = extension.Executable,
            Details = Describe(extension.Payload),
        };

    private static string? Describe(ExtensionPayload? payload) => payload switch
    {
        FileTypeAssociationExtension fta =>
            $"{fta.Name}: {string.Join(" ", fta.FileTypes.Select(static t => t.Extension))}".TrimEnd(),
        ProtocolExtension protocol => $"{protocol.Name}:",
        AppExecutionAliasExtension alias => string.Join(" ", alias.Aliases),
        StartupTaskExtension task =>
            task.IsEnabled is bool enabled ? $"{task.TaskId} (enabled={enabled})" : task.TaskId,
        FullTrustProcessExtension process =>
            string.Join(" ", process.ParameterGroups.Select(static g => g.GroupId)),
        ComServerExtension com => DescribeComServer(com),
        ShortcutExtension shortcut => shortcut.File,
        _ => null,
    };

    private static string DescribeComServer(ComServerExtension com)
    {
        IEnumerable<ComClass> classes = com.ExeServers
            .SelectMany(static server => server.Classes)
            .Concat(com.SurrogateServers.SelectMany(static server => server.Classes));

        return string.Join(" ", classes.Select(static c => c.Id));
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
        // Rendered from the categorized list so that a gated capability is visibly gated; falls back
        // to the flat names when the manifest predates categorization (never, in practice).
        string capabilities = r.DeclaredCapabilities.Count > 0
            ? string.Join(", ", r.DeclaredCapabilities.Select(static c =>
                c.Kind == "general" ? c.Name : $"{c.Name} ({c.Kind})"))
            : r.Capabilities.Count == 0 ? "(none)" : string.Join(", ", r.Capabilities);
        o.WriteLine($"Capabilities    : {capabilities}");
        if (r.Dependencies.Count > 0)
        {
            o.WriteLine("Dependencies    :");
            foreach (DependencyReport dependency in r.Dependencies)
            {
                string version = dependency.MinVersion is null ? "" : $" >= {dependency.MinVersion}";
                string optional = dependency.IsOptional ? " (optional)" : "";
                o.WriteLine($"  {dependency.Kind,-11} {dependency.Name}{version}{optional}");
            }
        }

        if (r.Extensions.Count > 0)
        {
            o.WriteLine("Extensions      :");
            foreach (ExtensionReport extension in r.Extensions)
            {
                string owner = extension.ApplicationId is null ? "package" : extension.ApplicationId;
                string details = extension.Details is null ? "" : $" {extension.Details}";
                o.WriteLine($"  [{owner}] {extension.Category}{details}");
            }
        }

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
