using System.Xml;
using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

/// <summary>
/// Parses an <c>AppxBundleManifest.xml</c> document into a <see cref="BundleManifest"/>.
/// </summary>
/// <remarks>Namespace-tolerant and hardened against XXE, like <see cref="AppxManifestParser"/>.</remarks>
public static class BundleManifestParser
{
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Parses a bundle manifest from a stream.</summary>
    /// <param name="manifestStream">A readable stream over an <c>AppxBundleManifest.xml</c> document.</param>
    /// <returns>The parsed bundle manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifestStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The manifest is malformed or missing required data.</exception>
    public static BundleManifest Parse(Stream manifestStream)
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
            throw new InvalidDataException("The bundle manifest is not well-formed XML.", ex);
        }

        return Parse(document);
    }

    /// <summary>Parses a bundle manifest from an already-loaded XML document.</summary>
    /// <param name="document">The manifest document; its root must be a <c>Bundle</c> element.</param>
    /// <returns>The parsed bundle manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The manifest is malformed or missing required data.</exception>
    public static BundleManifest Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XElement root = document.Root
            ?? throw new InvalidDataException("The bundle manifest has no root element.");

        if (root.Name.LocalName != "Bundle")
        {
            throw new InvalidDataException(
                $"Expected a 'Bundle' root element but found '{root.Name.LocalName}'.");
        }

        return new BundleManifest
        {
            Identity = ParseIdentity(root),
            Packages = ParsePackages(root),
        };
    }

    private static PackageIdentity ParseIdentity(XElement root)
    {
        XElement identity = root.ElementByLocalName("Identity")
            ?? throw new InvalidDataException("The bundle manifest is missing the required 'Identity' element.");

        string name = identity.AttributeValue("Name")
            ?? throw new InvalidDataException("Bundle Identity is missing the required 'Name' attribute.");
        string publisher = identity.AttributeValue("Publisher")
            ?? throw new InvalidDataException("Bundle Identity is missing the required 'Publisher' attribute.");
        string versionText = identity.AttributeValue("Version")
            ?? throw new InvalidDataException("Bundle Identity is missing the required 'Version' attribute.");

        if (!ManifestVersion.TryParse(versionText, out Version version))
        {
            throw new InvalidDataException(
                $"Bundle Identity has an invalid MSIX version '{versionText}'. Expected four components, each 0-65535.");
        }

        return new PackageIdentity
        {
            Name = name,
            Publisher = publisher,
            Version = version,
            Architecture = ProcessorArchitecture.Neutral,
        };
    }

    private static List<BundlePackageEntry> ParsePackages(XElement root)
    {
        XElement packages = root.ElementByLocalName("Packages")
            ?? throw new InvalidDataException("The bundle manifest is missing the required 'Packages' element.");

        var result = new List<BundlePackageEntry>();
        foreach (XElement package in packages.ElementsByLocalName("Package"))
        {
            string fileName = package.AttributeValue("FileName")
                ?? throw new InvalidDataException("A bundle 'Package' is missing the required 'FileName' attribute.");
            Version version = ManifestVersion.Parse(
                package.AttributeValue("Version"), $"Bundle package '{fileName}' Version");

            BundlePackageType type = ParsePackageType(package.AttributeValue("Type"), fileName);

            List<BundleResource> resources = package
                .ElementByLocalName("Resources")?
                .ElementsByLocalName("Resource")
                .Select(r => new BundleResource
                {
                    Language = r.AttributeValue("Language"),
                    Scale = r.AttributeValue("Scale"),
                    DXFeatureLevel = r.AttributeValue("DXFeatureLevel"),
                })
                .Where(r => r.Language is not null || r.Scale is not null || r.DXFeatureLevel is not null)
                .ToList() ?? [];

            result.Add(new BundlePackageEntry
            {
                FileName = fileName,
                Type = type,
                Version = version,
                Architecture = AppxManifestParser.ParseArchitecture(package.AttributeValue("Architecture")),
                ResourceId = package.AttributeValue("ResourceId") ?? string.Empty,
                Resources = resources,
                Offset = ParseNonNegativeInt64(package.AttributeValue("Offset"), fileName, "Offset"),
                Size = ParseNonNegativeInt64(package.AttributeValue("Size"), fileName, "Size"),
                TargetDeviceFamilies = ParseTargetDeviceFamilies(package, fileName),
            });
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("The bundle manifest declares no packages.");
        }

        return result;
    }

    private static long ParseNonNegativeInt64(string? value, string fileName, string attributeName)
    {
        if (value is null)
        {
            return 0;
        }

        if (!long.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out long result)
            || result < 0)
        {
            throw new InvalidDataException(
                $"Bundle package '{fileName}' has an invalid {attributeName} '{value}'.");
        }

        return result;
    }

    private static List<TargetDeviceFamily> ParseTargetDeviceFamilies(XElement package, string fileName)
    {
        XElement? dependencies = package.ElementByLocalName("Dependencies");
        if (dependencies is null)
        {
            return [];
        }

        var result = new List<TargetDeviceFamily>();
        foreach (XElement family in dependencies.ElementsByLocalName("TargetDeviceFamily"))
        {
            string name = family.AttributeValue("Name")
                ?? throw new InvalidDataException(
                    $"Bundle package '{fileName}' has a TargetDeviceFamily without a Name.");
            result.Add(new TargetDeviceFamily
            {
                Name = name,
                MinVersion = ManifestVersion.Parse(
                    family.AttributeValue("MinVersion"),
                    $"Bundle package '{fileName}' TargetDeviceFamily '{name}' MinVersion"),
                MaxVersionTested = ManifestVersion.Parse(
                    family.AttributeValue("MaxVersionTested"),
                    $"Bundle package '{fileName}' TargetDeviceFamily '{name}' MaxVersionTested"),
            });
        }

        return result;
    }

    private static BundlePackageType ParsePackageType(string? value, string fileName)
    {
        // The bundle schema defaults an omitted Type to 'resource'.
        if (string.IsNullOrEmpty(value) || string.Equals(value, "resource", StringComparison.OrdinalIgnoreCase))
        {
            return BundlePackageType.Resource;
        }

        if (string.Equals(value, "application", StringComparison.OrdinalIgnoreCase))
        {
            return BundlePackageType.Application;
        }

        throw new InvalidDataException($"Bundle package '{fileName}' has an invalid Type '{value}'.");
    }
}
