using System.Xml;
using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

/// <summary>
/// Parses an <c>AppxManifest.xml</c> document into an <see cref="AppxManifest"/>.
/// </summary>
/// <remarks>
/// Parsing is namespace-tolerant (elements are matched by local name) so a single implementation
/// works across MSIX schema revisions. XML is read with DTD processing disabled and no external
/// resolver to avoid XXE and entity-expansion attacks.
/// </remarks>
public static class AppxManifestParser
{
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Parses a manifest from a stream.</summary>
    /// <param name="manifestStream">A readable stream over an <c>AppxManifest.xml</c> document.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifestStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The manifest is malformed or missing required data.</exception>
    public static AppxManifest Parse(Stream manifestStream)
    {
        ArgumentNullException.ThrowIfNull(manifestStream);

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(manifestStream, SafeReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw MsixError.Format(MsixErrorCode.Xml, "The manifest is not well-formed XML.", ex);
        }

        return Parse(document);
    }

    /// <summary>Parses a manifest from an already-loaded XML document.</summary>
    /// <param name="document">The manifest document; its root must be a <c>Package</c> element.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The manifest is malformed or missing required data.</exception>
    public static AppxManifest Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "The manifest has no root element.");

        if (root.Name.LocalName != "Package")
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"Expected a 'Package' root element but found '{root.Name.LocalName}'.");
        }

        PackageIdentity identity = ParseIdentity(root);
        XElement? properties = root.ElementByLocalName("Properties");

        return new AppxManifest
        {
            Identity = identity,
            DisplayName = properties?.ElementByLocalName("DisplayName")?.Value.Trim() ?? string.Empty,
            PublisherDisplayName = properties?.ElementByLocalName("PublisherDisplayName")?.Value.Trim() ?? string.Empty,
            Description = properties?.ElementByLocalName("Description")?.Value.Trim(),
            Logo = NullIfEmpty(properties?.ElementByLocalName("Logo")?.Value.Trim()),
            IsFramework = ParseFrameworkFlag(properties?.ElementByLocalName("Framework")?.Value),
            IsResourcePackage = ParseFrameworkFlag(properties?.ElementByLocalName("ResourcePackage")?.Value),
            Resources = ParseResources(root),
            Capabilities = ParseCapabilities(root),
            Applications = ParseApplications(root),
            TargetDeviceFamilies = ParseTargetDeviceFamilies(root),
        };
    }

    private static PackageIdentity ParseIdentity(XElement root)
    {
        XElement identity = root.ElementByLocalName("Identity")
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "The manifest is missing the required 'Identity' element.");

        string name = identity.AttributeValue("Name")
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "Identity is missing the required 'Name' attribute.");
        string publisher = identity.AttributeValue("Publisher")
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "Identity is missing the required 'Publisher' attribute.");
        string versionText = identity.AttributeValue("Version")
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "Identity is missing the required 'Version' attribute.");

        if (!ManifestVersion.TryParse(versionText, out Version version))
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"Identity has an invalid MSIX version '{versionText}'. Expected four components, each 0-65535.");
        }

        return new PackageIdentity
        {
            Name = name,
            Publisher = publisher,
            Version = version,
            Architecture = ParseArchitecture(identity.AttributeValue("ProcessorArchitecture")),
            ResourceId = identity.AttributeValue("ResourceId") ?? string.Empty,
        };
    }

    internal static ProcessorArchitecture ParseArchitecture(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "" or "neutral" => ProcessorArchitecture.Neutral,
            "x86" => ProcessorArchitecture.X86,
            "x64" => ProcessorArchitecture.X64,
            "arm" => ProcessorArchitecture.Arm,
            "arm64" => ProcessorArchitecture.Arm64,
            "x86a64" or "x86onarm64" => ProcessorArchitecture.X86OnArm64,
            _ => ProcessorArchitecture.Unknown,
        };

    private static List<string> ParseCapabilities(XElement root)
    {
        XElement? capabilities = root.ElementByLocalName("Capabilities");
        if (capabilities is null)
        {
            return [];
        }

        // Collect the Name of every capability-like child (Capability, DeviceCapability,
        // RestrictedCapability, CustomCapability, ...), preserving order and de-duplicating.
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement child in capabilities.Elements())
        {
            string? name = child.AttributeValue("Name");
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static List<BundleResource> ParseResources(XElement root) =>
        root.ElementByLocalName("Resources")?
            .ElementsByLocalName("Resource")
            .Select(static resource => new BundleResource
            {
                Language = resource.AttributeValue("Language"),
                Scale = resource.AttributeValue("Scale"),
                DXFeatureLevel = resource.AttributeValue("DXFeatureLevel"),
            })
            .Where(static resource =>
                resource.Language is not null
                || resource.Scale is not null
                || resource.DXFeatureLevel is not null)
            .ToList() ?? [];

    private static List<ManifestApplication> ParseApplications(XElement root)
    {
        XElement? applications = root.ElementByLocalName("Applications");
        if (applications is null)
        {
            return [];
        }

        var result = new List<ManifestApplication>();
        foreach (XElement app in applications.ElementsByLocalName("Application"))
        {
            string id = app.AttributeValue("Id")
                ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "An 'Application' element is missing the required 'Id' attribute.");

            result.Add(new ManifestApplication
            {
                Id = id,
                Executable = NullIfEmpty(app.AttributeValue("Executable")),
                EntryPoint = NullIfEmpty(app.AttributeValue("EntryPoint")),
                VisualElements = ParseVisualElements(app.ElementByLocalName("VisualElements")),
            });
        }

        return result;
    }

    private static VisualElements ParseVisualElements(XElement? element)
    {
        if (element is null)
        {
            return new VisualElements();
        }

        return new VisualElements
        {
            DisplayName = element.AttributeValue("DisplayName") ?? string.Empty,
            Description = element.AttributeValue("Description") ?? string.Empty,
            Square150x150Logo = NullIfEmpty(element.AttributeValue("Square150x150Logo")),
            Square44x44Logo = NullIfEmpty(element.AttributeValue("Square44x44Logo")),
            BackgroundColor = NullIfEmpty(element.AttributeValue("BackgroundColor")),
            AppListEntry = !string.Equals(element.AttributeValue("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static List<TargetDeviceFamily> ParseTargetDeviceFamilies(XElement root)
    {
        XElement? dependencies = root.ElementByLocalName("Dependencies");
        if (dependencies is null)
        {
            return [];
        }

        var result = new List<TargetDeviceFamily>();
        foreach (XElement tdf in dependencies.ElementsByLocalName("TargetDeviceFamily"))
        {
            string name = tdf.AttributeValue("Name")
                ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics, "A 'TargetDeviceFamily' is missing the required 'Name' attribute.");
            Version minVersion = ManifestVersion.Parse(
                tdf.AttributeValue("MinVersion"),
                $"TargetDeviceFamily '{name}' MinVersion",
                MsixErrorCode.ManifestSemantics);
            Version maxTested = ManifestVersion.Parse(
                tdf.AttributeValue("MaxVersionTested"),
                $"TargetDeviceFamily '{name}' MaxVersionTested",
                MsixErrorCode.ManifestSemantics);

            result.Add(new TargetDeviceFamily
            {
                Name = name,
                MinVersion = minVersion,
                MaxVersionTested = maxTested,
            });
        }

        return result;
    }

    private static bool ParseFrameworkFlag(string? value)
    {
        string? trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        try
        {
            return XmlConvert.ToBoolean(trimmed);
        }
        catch (FormatException ex)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics, $"Properties/Framework has an invalid boolean value '{value}'.", ex);
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
