using System.Text;
using System.Xml;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Authoring;

internal static class ContentTypesWriter
{
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string ManifestContentType = "application/vnd.ms-appx.manifest+xml";
    private const string BlockMapContentType = "application/vnd.ms-appx.blockmap+xml";
    private const string GenericContentType = "application/octet-stream";

    private static readonly Dictionary<string, string> KnownContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["css"] = "text/css",
            ["gif"] = "image/gif",
            ["htm"] = "text/html",
            ["html"] = "text/html",
            ["jpeg"] = "image/jpeg",
            ["jpg"] = "image/jpeg",
            ["json"] = "application/json",
            ["png"] = "image/png",
            ["svg"] = "image/svg+xml",
            ["txt"] = "text/plain",
            ["xml"] = "application/xml",
        };

    public static byte[] Write(IEnumerable<string> payloadPartNames)
    {
        ArgumentNullException.ThrowIfNull(payloadPartNames);

        string[] partNames = payloadPartNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] extensions = partNames
            .Select(GetExtension)
            .Where(static extension => extension.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] extensionlessParts = partNames
            .Where(static name => GetExtension(name).Length == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(output, CreateSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Types", ContentTypesNamespace);

            foreach (string extension in extensions)
            {
                writer.WriteStartElement("Default", ContentTypesNamespace);
                writer.WriteAttributeString("Extension", extension);
                writer.WriteAttributeString("ContentType", GetContentType(extension));
                writer.WriteEndElement();
            }

            foreach (string partName in extensionlessParts)
            {
                WriteOverride(writer, partName, GenericContentType);
            }

            WriteOverride(writer, OpcPartNames.AppxManifest, ManifestContentType);
            WriteOverride(writer, OpcPartNames.AppxBlockMap, BlockMapContentType);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    private static string GetExtension(string partName)
    {
        string fileName = partName[(partName.LastIndexOf('/') + 1)..];
        int dot = fileName.LastIndexOf('.');
        return dot < 0 || dot == fileName.Length - 1 ? string.Empty : fileName[(dot + 1)..].ToLowerInvariant();
    }

    private static string GetContentType(string extension) =>
        KnownContentTypes.TryGetValue(extension, out string? contentType) ? contentType : GenericContentType;

    private static void WriteOverride(XmlWriter writer, string partName, string contentType)
    {
        writer.WriteStartElement("Override", ContentTypesNamespace);
        writer.WriteAttributeString("PartName", "/" + OpcPartNameEncoder.Encode(partName));
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static XmlWriterSettings CreateSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
