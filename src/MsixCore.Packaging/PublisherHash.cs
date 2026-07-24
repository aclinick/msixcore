using System.Security.Cryptography;
using System.Text;

namespace MsixCore.Packaging;

/// <summary>
/// Computes the publisher hash used in MSIX/APPX package family and full names.
/// </summary>
/// <remarks>
/// The algorithm (as implemented by Windows) is:
/// <list type="number">
/// <item>Encode the full publisher distinguished name as UTF-16LE.</item>
/// <item>Compute its SHA-256 digest and take the first 8 bytes (64 bits).</item>
/// <item>Append a single zero bit to make 65 bits, then encode as 13 characters using the
/// MSIX Base32 alphabet <c>0123456789abcdefghjkmnpqrstvwxyz</c> (digits + lowercase letters,
/// excluding <c>i</c>, <c>l</c>, <c>o</c>, <c>u</c>).</item>
/// </list>
/// For example the publisher
/// <c>CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US</c>
/// hashes to <c>8wekyb3d8bbwe</c>.
/// </remarks>
public static class PublisherHash
{
    private const string Base32Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>Computes the 13-character publisher hash for the given publisher name.</summary>
    /// <param name="publisher">The full publisher distinguished name from the package identity.</param>
    /// <returns>The 13-character lowercase Base32 publisher hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="publisher"/> is <see langword="null"/>.</exception>
    public static string Compute(string publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        byte[] digest = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));

        // Build a 65-bit big-endian bit string from the first 8 digest bytes plus one trailing 0 bit.
        var bits = new StringBuilder(65);
        for (int i = 0; i < 8; i++)
        {
            bits.Append(Convert.ToString(digest[i], 2).PadLeft(8, '0'));
        }

        bits.Append('0');

        // Encode as 13 groups of 5 bits.
        var result = new char[13];
        for (int group = 0; group < 13; group++)
        {
            int fiveBits = Convert.ToInt32(bits.ToString(group * 5, 5), 2);
            result[group] = Base32Alphabet[fiveBits];
        }

        return new string(result);
    }
}
