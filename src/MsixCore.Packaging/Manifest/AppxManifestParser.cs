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
            throw new InvalidDataException("The manifest is not well-formed XML.", ex);
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
            ?? throw new InvalidDataException("The manifest has no root element.");

        if (root.Name.LocalName != "Package")
        {
            throw new InvalidDataException(
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
            IsFramework = string.Equals(
                properties?.ElementByLocalName("Framework")?.Value.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase),
            Capabilities = ParseCapabilities(root),
            Applications = ParseApplications(root),
            TargetDeviceFamilies = ParseTargetDeviceFamilies(root),
        };
    }

    private static PackageIdentity ParseIdentity(XElement root)
    {
        XElement identity = root.ElementByLocalName("Identity")
            ?? throw new InvalidDataException("The manifest is missing the required 'Identity' element.");

        string name = identity.AttributeValue("Name")
            ?? throw new InvalidDataException("Identity is missing the required 'Name' attribute.");
        string publisher = identity.AttributeValue("Publisher")
            ?? throw new InvalidDataException("Identity is missing the required 'Publisher' attribute.");
        string versionText = identity.AttributeValue("Version")
            ?? throw new InvalidDataException("Identity is missing the required 'Version' attribute.");

        if (!Version.TryParse(versionText, out Version? version))
        {
            throw new InvalidDataException($"Identity has an invalid Version '{versionText}'.");
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
                ?? throw new InvalidDataException("An 'Application' element is missing the required 'Id' attribute.");

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
            string? name = tdf.AttributeValue("Name");
            string? minVersionText = tdf.AttributeValue("MinVersion");
            if (string.IsNullOrEmpty(name) || !Version.TryParse(minVersionText, out Version? minVersion))
            {
                // Skip malformed dependency entries rather than failing the whole manifest.
                continue;
            }

            Version? maxTested = Version.TryParse(tdf.AttributeValue("MaxVersionTested"), out Version? mvt)
                ? mvt
                : null;

            result.Add(new TargetDeviceFamily
            {
                Name = name,
                MinVersion = minVersion,
                MaxVersionTested = maxTested,
            });
        }

        return result;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
