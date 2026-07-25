using System.Globalization;
using System.Security.Cryptography;
using MsixCore.Packaging.Opc;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Compares unpacked payload content by SHA-256 hashes.</summary>
public sealed class PayloadHashComparer
{
    /// <summary>Compares the non-footprint package parts in two package files.</summary>
    public static PayloadHashComparison ComparePackages(string leftPackagePath, string rightPackagePath)
    {
        using OpcPackage left = OpcPackage.Open(leftPackagePath);
        using OpcPackage right = OpcPackage.Open(rightPackagePath);
        Dictionary<string, string> leftHashes = HashPayload(left);
        Dictionary<string, string> rightHashes = HashPayload(right);
        var differences = new List<string>();

        foreach (string name in leftHashes.Keys.Union(rightHashes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.Ordinal))
        {
            bool hasLeft = leftHashes.TryGetValue(name, out string? leftHash);
            bool hasRight = rightHashes.TryGetValue(name, out string? rightHash);
            if (!hasLeft || !hasRight)
            {
                differences.Add(name + ": " + (hasLeft ? "present" : "missing") + " vs " + (hasRight ? "present" : "missing"));
                continue;
            }

            if (!string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(name + ": SHA-256 " + leftHash + " vs " + rightHash);
            }
        }

        return new PayloadHashComparison(differences.Count == 0, differences);
    }

    private static Dictionary<string, string> HashPayload(OpcPackage package)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string partName in package.PartNames)
        {
            if (PackageFootprints.IsFootprint(partName))
            {
                continue;
            }

            using Stream stream = package.OpenPart(partName);
            byte[] hash = SHA256.HashData(stream);
            hashes.Add(partName, Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture));
        }

        return hashes;
    }
}

/// <summary>Payload hash comparison result.</summary>
public sealed record PayloadHashComparison(bool IsEquivalent, IReadOnlyList<string> Differences);
