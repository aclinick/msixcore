using System.Text;
using System.Text.RegularExpressions;

namespace MsixCore.Deployment;

/// <summary>
/// Matches strings against glob-style patterns using <c>*</c> (any run of characters, including
/// none) and <c>?</c> (exactly one character). Matching is case-insensitive and whole-string.
/// </summary>
internal static class Wildcard
{
    public static bool IsMatch(string pattern, string input)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(input);

        // Fast path: no wildcards means an ordinary case-insensitive equality check.
        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            return string.Equals(pattern, input, StringComparison.OrdinalIgnoreCase);
        }

        return Regex.IsMatch(
            input,
            Translate(pattern),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static string Translate(string pattern)
    {
        var builder = new StringBuilder(pattern.Length + 4);
        builder.Append('^');
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return builder.ToString();
    }
}
