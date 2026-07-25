using System.Globalization;
using System.Text;

namespace MsixCore.Packaging.Authoring;

internal static class OpcPartNameEncoder
{
    private const string ReservedCharacters = " !+#%{}^`@&[]";

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

        var encoded = new StringBuilder(segment.Length);
        foreach (char character in segment)
        {
            if (ReservedCharacters.Contains(character, StringComparison.Ordinal))
            {
                encoded.Append('%');
                encoded.Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                encoded.Append(character);
            }
        }

        return encoded.ToString();
    }
}
