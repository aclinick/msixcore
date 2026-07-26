using System.Security.Cryptography;
using System.Text;

namespace MsixKit.Tests;

/// <summary>Builds loose (unpacked) package folders with a matching <c>AppxBlockMap.xml</c> for CLI tests.</summary>
internal static class LooseCliPackage
{
    public const string ManifestName = "AppxManifest.xml";

    public static string ManifestXml(
        string name = "Contoso.MyApp",
        string publisher = "CN=Contoso",
        string version = "1.2.3.4",
        string architecture = "x64") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="{name}" Publisher="{publisher}" Version="{version}" ProcessorArchitecture="{architecture}" />
          <Properties>
            <DisplayName>Contoso My App</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
          </Properties>
        </Package>
        """;

    /// <summary>Creates a loose package directory containing the given payload plus a valid block map.</summary>
    public static string Create(
        string root,
        string folder,
        IReadOnlyDictionary<string, byte[]>? extra = null,
        string? manifestXml = null)
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);

        var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestName] = Encoding.UTF8.GetBytes(manifestXml ?? ManifestXml()),
        };
        if (extra is not null)
        {
            foreach ((string k, byte[] v) in extra)
            {
                payload[k] = v;
            }
        }

        foreach ((string relative, byte[] content) in payload)
        {
            string full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        File.WriteAllText(Path.Combine(dir, "AppxBlockMap.xml"), BlockMapXml(payload), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "[Content_Types].xml"), ContentTypesXml(payload.Keys), Encoding.UTF8);
        return dir;
    }

    /// <summary>Writes a block map that intentionally does not match the payload (for INVALID tests).</summary>
    public static void CorruptBlockMap(string dir)
    {
        string path = Path.Combine(dir, "AppxBlockMap.xml");
        string content = File.ReadAllText(path);
        // Flip the first base64 hash character so a block hash no longer matches.
        int idx = content.IndexOf("Hash=\"", StringComparison.Ordinal) + 6;
        char c = content[idx];
        content = content.Remove(idx, 1).Insert(idx, c == 'A' ? "B" : "A");
        File.WriteAllText(path, content);
    }

    private static string BlockMapXml(IReadOnlyDictionary<string, byte[]> payload)
    {
        var sb = new StringBuilder();
        sb.Append("<BlockMap xmlns=\"http://schemas.microsoft.com/appx/2010/blockmap\" ");
        sb.Append("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");
        foreach ((string name, byte[] content) in payload)
        {
            string size = content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("<File Name=\"").Append(name.Replace('/', '\\')).Append("\" Size=\"").Append(size).Append("\" LfhSize=\"0\">");
            for (int offset = 0; offset < content.Length; offset += 65536)
            {
                int length = Math.Min(65536, content.Length - offset);
                byte[] hash = SHA256.HashData(content.AsSpan(offset, length));
                sb.Append("<Block Hash=\"").Append(Convert.ToBase64String(hash)).Append("\" />");
            }

            sb.Append("</File>");
        }

        sb.Append("</BlockMap>");
        return sb.ToString();
    }

    private static string ContentTypesXml(IEnumerable<string> partNames)
    {
        string[] extensions = partNames
            .Append("AppxBlockMap.xml")
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

        return xml.Append("</Types>").ToString();
    }
}
