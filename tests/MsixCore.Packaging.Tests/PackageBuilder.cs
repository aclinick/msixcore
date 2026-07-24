using System.IO.Compression;
using System.Security.Cryptography;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

/// <summary>Test helpers for constructing in-memory OPC packages and matching block maps.</summary>
internal static class PackageBuilder
{
    public static OpcPackage OpcFrom(IReadOnlyDictionary<string, byte[]> parts)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in parts)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return OpcPackage.Open(stream, leaveOpen: false);
    }

    public static BlockMapFile BlockMapFileFor(string name, byte[] content)
    {
        var blocks = new List<BlockMapBlock>();
        for (int offset = 0; offset < content.Length; offset += BlockMap.BlockSize)
        {
            int length = Math.Min(BlockMap.BlockSize, content.Length - offset);
            byte[] hash = SHA256.HashData(content.AsSpan(offset, length));
            blocks.Add(new BlockMapBlock { Hash = Convert.ToBase64String(hash) });
        }

        return new BlockMapFile { Name = name, Size = content.Length, Blocks = blocks };
    }

    public static BlockMap BlockMapFor(IReadOnlyDictionary<string, byte[]> parts)
    {
        var files = new List<BlockMapFile>();
        foreach ((string name, byte[] content) in parts)
        {
            files.Add(BlockMapFileFor(name, content));
        }

        return new BlockMap { HashMethod = BlockMapHashMethod.Sha256, Files = files };
    }

    /// <summary>Serializes a matching <c>AppxBlockMap.xml</c> for the given payload parts.</summary>
    public static string BlockMapXml(IReadOnlyDictionary<string, byte[]> parts)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<BlockMap xmlns=\"http://schemas.microsoft.com/appx/2010/blockmap\" ");
        sb.Append("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");
        foreach (BlockMapFile file in BlockMapFor(parts).Files)
        {
            string size = file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("<File Name=\"").Append(file.Name.Replace('/', '\\')).Append("\" Size=\"").Append(size).Append("\" LfhSize=\"0\">");
            foreach (BlockMapBlock block in file.Blocks)
            {
                sb.Append("<Block Hash=\"").Append(block.Hash).Append("\" />");
            }

            sb.Append("</File>");
        }

        sb.Append("</BlockMap>");
        return sb.ToString();
    }
}
