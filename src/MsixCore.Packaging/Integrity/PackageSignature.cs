using System.Security.Cryptography.X509Certificates;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// The signer identity and integrity status extracted from an <c>AppxSignature.p7x</c>.
/// </summary>
/// <remarks>
/// This is a snapshot of the primary signer; it does not retain the live certificate. Trust-chain
/// evaluation (which is environment- and policy-dependent) is intentionally separate from this
/// content-integrity view.
/// </remarks>
public sealed record PackageSignature
{
    /// <summary>The signer certificate subject distinguished name (the package publisher DN).</summary>
    public required string SubjectName { get; init; }

    /// <summary>The signer certificate issuer distinguished name.</summary>
    public required string IssuerName { get; init; }

    /// <summary>The signer certificate SHA-1 thumbprint (uppercase hex).</summary>
    public required string Thumbprint { get; init; }

    /// <summary>The signer certificate validity start.</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>The signer certificate validity end.</summary>
    public required DateTimeOffset NotAfter { get; init; }

    /// <summary>
    /// Whether the CMS/PKCS#7 signature is internally consistent (the signed digest matches and the
    /// signature verifies against the signer's public key). This does <em>not</em> assert trust.
    /// </summary>
    public required bool IsSignatureValid { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> if the signer subject DN matches the given manifest
    /// <c>Publisher</c>, comparing canonicalized X.500 distinguished names. MSIX requires the manifest
    /// publisher to equal the signing certificate subject.
    /// </summary>
    /// <param name="manifestPublisher">The <c>Identity/@Publisher</c> value from the manifest.</param>
    public bool MatchesPublisher(string manifestPublisher)
    {
        ArgumentNullException.ThrowIfNull(manifestPublisher);

        try
        {
            byte[] fromManifest = new X500DistinguishedName(manifestPublisher).RawData;
            byte[] fromSigner = new X500DistinguishedName(SubjectName).RawData;
            return fromManifest.AsSpan().SequenceEqual(fromSigner);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Fall back to an ordinal comparison when a value is not a parseable DN.
            return string.Equals(manifestPublisher, SubjectName, StringComparison.Ordinal);
        }
    }
}
