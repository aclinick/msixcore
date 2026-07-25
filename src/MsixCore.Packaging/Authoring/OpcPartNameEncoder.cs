using System.Globalization;
using System.Text;

namespace MsixCore.Packaging.Authoring;

internal static class OpcPartNameEncoder
{
    private const string ReservedCharacters = " !+#%{}^`@&[]";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Encode(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);

        string[] segments = partName.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = EncodeSegment(segments[i]);
        }

        return string.Join('/', segments);
    }

    public static string EncodeSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        byte[] utf8Bytes = StrictUtf8.GetBytes(segment);
        var encoded = new StringBuilder(utf8Bytes.Length);
        foreach (byte value in utf8Bytes)
        {
            if (value >= 0x80 || ReservedCharacters.Contains((char)value, StringComparison.Ordinal))
            {
                encoded.Append('%');
                encoded.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append((char)value);
            }
        }

        return encoded.ToString();
    }
}
