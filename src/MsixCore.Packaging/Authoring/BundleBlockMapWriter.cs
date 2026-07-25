using System.Globalization;
using System.Text;
using System.Xml;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Authoring;

internal static class BundleBlockMapWriter
{
    private const string BlockMapNamespace = "http://schemas.microsoft.com/appx/2010/blockmap";
    private const string BlockMap2021Namespace = "http://schemas.microsoft.com/appx/2021/blockmap";
    private const string Sha256Uri = "http://www.w3.org/2001/04/xmlenc#sha256";

    public static byte[] Write(AuthoredBlockMapFile authoredFile)
    {
        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(output, CreateSettings()))
        {
            writer.WriteStartDocument(standalone: false);
            writer.WriteStartElement("BlockMap", BlockMapNamespace);
            writer.WriteAttributeString("xmlns", "b4", null, BlockMap2021Namespace);
            writer.WriteAttributeString("IgnorableNamespaces", "b4");
            writer.WriteAttributeString("HashMethod", Sha256Uri);

            BlockMapFile file = authoredFile.File;
            writer.WriteStartElement("File", BlockMapNamespace);
            writer.WriteAttributeString("Name", file.Name.Replace('/', '\\'));
            writer.WriteAttributeString("Size", file.Size.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "LfhSize",
                authoredFile.LocalFileHeaderSize.ToString(CultureInfo.InvariantCulture));
            foreach (BlockMapBlock block in file.Blocks)
            {
                writer.WriteStartElement("Block", BlockMapNamespace);
                writer.WriteAttributeString("Hash", block.Hash);
                if (block.CompressedSize is long compressedSize)
                {
                    writer.WriteAttributeString(
                        "Size",
                        compressedSize.ToString(CultureInfo.InvariantCulture));
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    private static XmlWriterSettings CreateSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
