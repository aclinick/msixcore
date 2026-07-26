using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Deployment.Tests;

/// <summary>
/// Builds packed <c>.msix</c> files (OPC ZIP + a matching <c>AppxBlockMap.xml</c>) on disk so
/// deployment-layer tests can drive the real install path (<c>MsixPackage.Open(path)</c> +
/// <c>VerifyBlockMap()</c>).
/// </summary>
internal static class PackedMsixBuilder
{
    /// <summary>Writes a valid packed package with the given payload parts and returns its path.</summary>
    public static string Create(
        string directory,
        string fileName = "sample.msix",
        string? manifestXml = null,
        IReadOnlyDictionary<string, byte[]>? extraParts = null,
        bool validBlockMap = true)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [OpcPartNames.AppxManifest] = Encoding.UTF8.GetBytes(manifestXml ?? LoosePackageBuilder.ManifestXml()),
        };

        if (extraParts is not null)
        {
            foreach ((string name, byte[] content) in extraParts)
            {
                parts[name] = content;
            }
        }

        string blockMapXml = BlockMapXml(parts, validBlockMap);

        using var file = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach ((string name, byte[] content) in parts)
        {
            using Stream entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
            entry.Write(content);
        }

        using (Stream blockMap = archive.CreateEntry(OpcPartNames.AppxBlockMap, CompressionLevel.NoCompression).Open())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(blockMapXml);
            blockMap.Write(bytes);
        }

        using (Stream contentTypes = archive.CreateEntry(OpcPartNames.ContentTypes, CompressionLevel.NoCompression).Open())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(ContentTypesXml(parts.Keys));
            contentTypes.Write(bytes);
        }

        return path;
    }

    private static string ContentTypesXml(IEnumerable<string> partNames)
    {
        string[] extensions = partNames
            .Append(OpcPartNames.AppxBlockMap)
            .Select(static partName => Path.GetExtension(partName) ?? string.Empty)
            .Where(static extension => extension.Length > 1)
            .Select(static extension => extension[1..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var xml = new StringBuilder(
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        foreach (string extension in extensions)
        {
            xml.Append("<Default Extension=\"")
                .Append(extension)
                .Append("\" ContentType=\"application/octet-stream\"/>");
        }

        xml.Append("</Types>");
        return xml.ToString();
    }

    private static string BlockMapXml(IReadOnlyDictionary<string, byte[]> parts, bool valid)
    {
        var sb = new StringBuilder();
        sb.Append("<BlockMap xmlns=\"http://schemas.microsoft.com/appx/2010/blockmap\" ");
        sb.Append("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");

        foreach ((string name, byte[] content) in parts)
        {
            string size = content.Length.ToString(CultureInfo.InvariantCulture);
            sb.Append("<File Name=\"").Append(name.Replace('/', '\\')).Append("\" Size=\"").Append(size).Append("\" LfhSize=\"0\">");
            for (int offset = 0; offset < content.Length; offset += BlockMap.BlockSize)
            {
                int length = Math.Min(BlockMap.BlockSize, content.Length - offset);
                byte[] hash = SHA256.HashData(content.AsSpan(offset, length));
                string encoded = Convert.ToBase64String(hash);
                if (!valid)
                {
                    // Corrupt one character so the declared hash no longer matches the content.
                    char[] chars = encoded.ToCharArray();
                    chars[0] = chars[0] == 'A' ? 'B' : 'A';
                    encoded = new string(chars);
                }

                sb.Append("<Block Hash=\"").Append(encoded).Append("\" />");
            }

            sb.Append("</File>");
        }

        sb.Append("</BlockMap>");
        return sb.ToString();
    }
}
