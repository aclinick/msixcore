using System.Globalization;
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
public static partial class AppxManifestParser
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
        List<ManifestCapability> capabilities = ParseCapabilities(root);

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
            Capabilities = ToCapabilityNames(capabilities),
            DeclaredCapabilities = capabilities,
            Applications = ParseApplications(root),
            TargetDeviceFamilies = ParseTargetDeviceFamilies(root),
            PackageDependencies = ParsePackageDependencies(root),
            Extensions = ParseExtensions(root),
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
                Extensions = ParseExtensions(app),
            });
        }

        return result;
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

    /// <summary>
    /// Parses the package-to-package dependencies declared under <c>Dependencies</c>.
    /// </summary>
    /// <remarks>
    /// Elements are matched by local name, so the revisioned forms of each element collapse onto one
    /// kind: both <c>uap3:MainPackageDependency</c> and <c>uap4:MainPackageDependency</c> yield
    /// <see cref="PackageDependencyKind.MainPackage"/>, and both <c>uap10:</c> and
    /// <c>uap13:HostRuntimeDependency</c> yield <see cref="PackageDependencyKind.HostRuntime"/>.
    /// This is deliberate: the revisions differ in which attributes they permit, not in what the
    /// relationship means, and a consumer that cared about the revision could read the namespace
    /// from the DOM instead.
    /// </remarks>
    private static List<PackageDependency> ParsePackageDependencies(XElement root)
    {
        XElement? dependencies = root.ElementByLocalName("Dependencies");
        if (dependencies is null)
        {
            return [];
        }

        var result = new List<PackageDependency>();
        foreach (XElement element in dependencies.Elements())
        {
            PackageDependencyKind kind;
            switch (element.Name.LocalName)
            {
                case "PackageDependency":
                    kind = PackageDependencyKind.Framework;
                    break;
                case "MainPackageDependency":
                    kind = PackageDependencyKind.MainPackage;
                    break;
                case "HostRuntimeDependency":
                    kind = PackageDependencyKind.HostRuntime;
                    break;
                default:
                    // TargetDeviceFamily is parsed separately; DriverDependency and
                    // OSPackageDependency are not package-to-package relationships and are not
                    // modelled yet. Unknown children are ignored for forward compatibility.
                    continue;
            }

            result.Add(ParsePackageDependency(element, kind));
        }

        return result;
    }

    private static PackageDependency ParsePackageDependency(XElement element, PackageDependencyKind kind)
    {
        string elementName = element.Name.LocalName;
        string name = NullIfEmpty(element.AttributeValue("Name"))
            ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"A '{elementName}' is missing the required 'Name' attribute.");

        // Publisher and MinVersion are required by the schema on PackageDependency and
        // HostRuntimeDependency, but MainPackageDependency has no version attribute at all and its
        // Publisher is optional.
        bool requiresPublisherAndVersion = kind != PackageDependencyKind.MainPackage;

        string? publisher = NullIfEmpty(element.AttributeValue("Publisher"));
        if (publisher is null && requiresPublisherAndVersion)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"'{elementName}' '{name}' is missing the required 'Publisher' attribute.");
        }

        Version? minVersion = null;
        if (requiresPublisherAndVersion)
        {
            minVersion = ManifestVersion.Parse(
                element.AttributeValue("MinVersion"),
                $"{elementName} '{name}' MinVersion",
                MsixErrorCode.ManifestSemantics);
        }

        return new PackageDependency
        {
            Kind = kind,
            Name = name,
            Publisher = publisher,
            MinVersion = minVersion,
            MaxMajorVersionTested = ParseMaxMajorVersionTested(element, elementName, name),
            IsOptional = ParseOptionalFlag(element, kind, elementName, name),
        };
    }

    /// <summary>
    /// Parses <c>uap6:Optional</c>, which marks a framework dependency the package can run without.
    /// </summary>
    /// <remarks>
    /// Only foundation <c>PackageDependency</c> declares this attribute. It is rejected on the other
    /// kinds rather than ignored: a modification package without its main package, or a hosted app
    /// without its host runtime, cannot run at all, so silently accepting <c>Optional="true"</c>
    /// there would let a malformed manifest opt out of a genuinely mandatory dependency.
    /// </remarks>
    private static bool ParseOptionalFlag(
        XElement element,
        PackageDependencyKind kind,
        string elementName,
        string name)
    {
        string? value = NullIfEmpty(element.AttributeValue("Optional")?.Trim());
        if (value is null)
        {
            return false;
        }

        if (kind != PackageDependencyKind.Framework)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"'{elementName}' '{name}' declares an 'Optional' attribute, which only 'PackageDependency' supports.");
        }

        try
        {
            return XmlConvert.ToBoolean(value);
        }
        catch (FormatException ex)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"{elementName} '{name}' has an invalid Optional value '{value}'.", ex);
        }
    }

    /// <summary>
    /// Parses <c>MaxMajorVersionTested</c>, which is a single unsigned 16-bit major version rather
    /// than the four-part quad used by every other version attribute in the manifest.
    /// </summary>
    private static ushort? ParseMaxMajorVersionTested(XElement element, string elementName, string name)
    {
        string? value = NullIfEmpty(element.AttributeValue("MaxMajorVersionTested"));
        if (value is null)
        {
            return null;
        }

        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort major))
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"{elementName} '{name}' has an invalid MaxMajorVersionTested '{value}'. Expected a whole number from 0 to 65535.");
        }

        return major;
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
