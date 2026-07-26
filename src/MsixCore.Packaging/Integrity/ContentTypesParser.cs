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
                string contentType = RequiredContentType(declaration);
                if (extension.StartsWith('.')
                    || extension.Contains('/')
                    || extension.Contains('\\')
                    || !IsToken(extension))
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
                string overrideContentType = RequiredContentType(declaration);
                if (!rawPartName.StartsWith('/')
                    || !OpcPackage.TryCanonicalizePartName(rawPartName[1..], out string canonical)
                    || !OpcPackage.IsValidPartName(canonical))
                {
                    throw new InvalidDataException(
                        $"Content-types Override has an invalid PartName '{rawPartName}'.");
                }

                if (!overrides.TryAdd(canonical, overrideContentType))
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

    /// <summary>
    /// Reads a <c>ContentType</c> attribute and checks it actually declares a media type.
    /// </summary>
    /// <remarks>
    /// Coverage is established by the presence of a declaration, so a syntactically meaningless value
    /// such as <c>ContentType=" "</c> would otherwise satisfy the coverage check while declaring no
    /// type at all. Requiring a well-formed <c>type/subtype</c> keeps "every part has a content type"
    /// an honest statement rather than a check on attribute presence.
    /// </remarks>
    private static string RequiredContentType(XElement element)
    {
        string value = RequiredAttribute(element, "ContentType");
        if (!IsValidContentType(value))
        {
            throw new InvalidDataException(
                $"Content-types '{element.Name.LocalName}' has an invalid ContentType '{value}'.");
        }

        return value;
    }

    private static bool IsValidContentType(string value)
    {
        // OPC ST_ContentType is a MIME media type: "type/subtype" with optional ';'-delimited parameters.
        ReadOnlySpan<char> media = value.AsSpan();
        int semicolon = media.IndexOf(';');
        if (semicolon >= 0)
        {
            media = media[..semicolon];
        }

        media = media.Trim();
        int slash = media.IndexOf('/');
        return slash > 0
            && slash < media.Length - 1
            && IsToken(media[..slash])
            && IsToken(media[(slash + 1)..]);
    }

    private static bool IsToken(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (char c in value)
        {
            // Printable US-ASCII excluding RFC 2616 separators (which covers whitespace and controls).
            if (c is < '!' or > '~' || "()<>@,;:\\\"/[]?=".Contains(c))
            {
                return false;
            }
        }

        return true;
    }
}
