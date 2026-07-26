using System.Xml;
using System.Xml.Linq;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Integrity;

/// <summary>Parses and validates the OPC <c>[Content_Types].xml</c> declarations.</summary>
public static class ContentTypesParser
{
    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>Parses content-type defaults and overrides from a stream.</summary>
    public static ContentTypesMap Parse(Stream contentTypesStream)
    {
        ArgumentNullException.ThrowIfNull(contentTypesStream);

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(contentTypesStream, SafeReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The content-types part is not well-formed XML.", ex);
        }

        XElement root = document.Root
            ?? throw new InvalidDataException("The content-types part has no root element.");
        XNamespace contentTypes = ContentTypesNamespace;
        if (root.Name != contentTypes + "Types")
        {
            throw new InvalidDataException(
                $"Expected a 'Types' root in the OPC content-types namespace but found '{root.Name}'.");
        }

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement declaration in root.Elements())
        {
            if (declaration.Name == contentTypes + "Default")
            {
                string extension = RequiredAttribute(declaration, "Extension");
                string contentType = RequiredAttribute(declaration, "ContentType");
                if (extension.StartsWith('.') || extension.Contains('/') || extension.Contains('\\'))
                {
                    throw new InvalidDataException(
                        $"Content-types Default has an invalid Extension '{extension}'.");
                }

                if (!defaults.TryAdd(extension, contentType))
                {
                    throw new InvalidDataException(
                        $"Content-types contains a duplicate Default for extension '{extension}'.");
                }
            }
            else if (declaration.Name == contentTypes + "Override")
            {
                string rawPartName = RequiredAttribute(declaration, "PartName");
                _ = RequiredAttribute(declaration, "ContentType");
                if (!rawPartName.StartsWith('/')
                    || !OpcPackage.TryCanonicalizePartName(rawPartName[1..], out string canonical)
                    || !OpcPackage.IsValidPartName(canonical))
                {
                    throw new InvalidDataException(
                        $"Content-types Override has an invalid PartName '{rawPartName}'.");
                }

                if (!overrides.TryAdd(canonical, declaration.AttributeValue("ContentType")!))
                {
                    throw new InvalidDataException(
                        $"Content-types contains a duplicate Override for part '{canonical}'.");
                }
            }
            else
            {
                throw new InvalidDataException(
                    $"Content-types contains an unexpected element '{declaration.Name}'.");
            }
        }

        return new ContentTypesMap { Defaults = defaults, Overrides = overrides };
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        string? value = element.AttributeValue(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException(
                $"Content-types '{element.Name.LocalName}' is missing the required '{name}' attribute.");
        }

        return value;
    }
}
