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

        if (!Version.TryParse(versionText, out Version? version))
        {
            throw new InvalidDataException($"Bundle Identity has an invalid Version '{versionText}'.");
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
        XElement? packages = root.ElementByLocalName("Packages");
        if (packages is null)
        {
            return [];
        }

        var result = new List<BundlePackageEntry>();
        foreach (XElement package in packages.ElementsByLocalName("Package"))
        {
            string? fileName = package.AttributeValue("FileName");
            string? versionText = package.AttributeValue("Version");
            if (string.IsNullOrEmpty(fileName) || !Version.TryParse(versionText, out Version? version))
            {
                // Skip malformed entries rather than failing the whole bundle.
                continue;
            }

            BundlePackageType type = string.Equals(
                package.AttributeValue("Type"), "resource", StringComparison.OrdinalIgnoreCase)
                ? BundlePackageType.Resource
                : BundlePackageType.Application;

            var resources = package
                .ElementByLocalName("Resources")?
                .ElementsByLocalName("Resource")
                .Select(r => r.AttributeValue("Language") ?? r.AttributeValue("Scale") ?? r.AttributeValue("DXFeatureLevel"))
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList() ?? [];

            result.Add(new BundlePackageEntry
            {
                FileName = fileName,
                Type = type,
                Version = version,
                Architecture = AppxManifestParser.ParseArchitecture(package.AttributeValue("Architecture")),
                ResourceId = package.AttributeValue("ResourceId") ?? string.Empty,
                Resources = resources,
            });
        }

        return result;
    }
}
