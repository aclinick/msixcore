using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Authoring;

internal static class BlockMapWriter
{
    private const string BlockMapNamespace = "http://schemas.microsoft.com/appx/2010/blockmap";
    private const string Sha256Uri = "http://www.w3.org/2001/04/xmlenc#sha256";

    public static BlockMapFile CopyAndHash(string name, Stream source, Stream destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] buffer = new byte[BlockMap.BlockSize];
        var blocks = new List<BlockMapBlock>();
        long size = 0;

        while (true)
        {
            int length = ReadBlock(source, buffer);
            if (length == 0)
            {
                break;
            }

            destination.Write(buffer, 0, length);
            byte[] hash = SHA256.HashData(buffer.AsSpan(0, length));
            blocks.Add(new BlockMapBlock { Hash = Convert.ToBase64String(hash) });
            size += length;
        }

        return new BlockMapFile { Name = name, Size = size, Blocks = blocks };
    }

    public static byte[] Write(IReadOnlyList<BlockMapFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(output, CreateSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("BlockMap", BlockMapNamespace);
            writer.WriteAttributeString("HashMethod", Sha256Uri);

            foreach (BlockMapFile file in files)
            {
                writer.WriteStartElement("File", BlockMapNamespace);
                writer.WriteAttributeString("Name", file.Name.Replace('/', '\\'));
                writer.WriteAttributeString("Size", file.Size.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("LfhSize", "0");

                foreach (BlockMapBlock block in file.Blocks)
                {
                    writer.WriteStartElement("Block", BlockMapNamespace);
                    writer.WriteAttributeString("Hash", block.Hash);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    private static int ReadBlock(Stream source, byte[] buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = source.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static XmlWriterSettings CreateSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
