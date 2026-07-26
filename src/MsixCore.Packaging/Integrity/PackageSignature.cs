using System.Security.Cryptography;
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

    /// <summary>
    /// The raw DER-encoded bytes of the signer certificate subject distinguished name. Retained
    /// verbatim from the certificate so that publisher matching can compare against the original ASN.1
    /// encoding and RDN ordering rather than a lossy re-encoding of the formatted <see cref="SubjectName"/>.
    /// </summary>
    public required ReadOnlyMemory<byte> SubjectNameRawData { get; init; }

    /// <summary>The signer certificate issuer distinguished name.</summary>
    public required string IssuerName { get; init; }

    /// <summary>The signer certificate SHA-1 thumbprint (uppercase hex).</summary>
    public required string Thumbprint { get; init; }

    /// <summary>The signer certificate validity start.</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>The signer certificate validity end.</summary>
    public required DateTimeOffset NotAfter { get; init; }

    /// <summary>
    /// Whether the CMS/PKCS#7 envelope is internally consistent (the signed digest matches its
    /// embedded content and the signature verifies against the signer's public key).
    /// </summary>
    /// <remarks>
    /// This asserts CMS-envelope integrity only. It does <em>not</em> assert that the signature is
    /// bound to this package's contents, nor that the signer is trusted. Callers gating a package must
    /// additionally verify the block map (<see cref="MsixPackage.VerifyBlockMap"/>), confirm the
    /// publisher via <see cref="MatchesPublisher(string)"/>, verify the APPX indirect-data digests
    /// via <see cref="DigestTable"/>, and (once implemented) verify the trust chain.
    /// </remarks>
    public required bool IsCmsIntegrityValid { get; init; }

    /// <summary>
    /// The parsed APPX digest table from the CMS <c>SpcIndirectDataContent</c>, or <see langword="null"/>
    /// if the CMS envelope was invalid (content cannot be trusted) or the table could not be parsed.
    /// When <see langword="null"/> and <see cref="IsCmsIntegrityValid"/> is <see langword="true"/>,
    /// check <see cref="DigestTableError"/> for the parse failure reason.
    /// </summary>
    public AppxDigestTable? DigestTable { get; init; }

    /// <summary>
    /// When <see cref="DigestTable"/> is <see langword="null"/> despite a valid CMS envelope,
    /// this contains the parse error message. <see langword="null"/> when the table parsed successfully
    /// or when the CMS envelope was invalid.
    /// </summary>
    public string? DigestTableError { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> if the signer subject DN matches the given manifest
    /// <c>Publisher</c>, comparing canonicalized X.500 distinguished names. MSIX requires the manifest
    /// publisher to equal the signing certificate subject.
    /// </summary>
    /// <param name="manifestPublisher">The <c>Identity/@Publisher</c> value from the manifest.</param>
    public bool MatchesPublisher(string manifestPublisher)
    {
        ArgumentNullException.ThrowIfNull(manifestPublisher);

        X500DistinguishedName manifestDn;
        X500DistinguishedName signerDn;
        try
        {
            manifestDn = new X500DistinguishedName(manifestPublisher);

            // Decode the signer DN from the certificate's original DER bytes when available, preserving
            // its exact RDN order and attribute value encodings. Re-parsing the formatted SubjectName
            // string would reorder/reformat RDNs and lose the encoding, producing false mismatches.
            signerDn = SubjectNameRawData.IsEmpty
                ? new X500DistinguishedName(SubjectName)
                : new X500DistinguishedName(SubjectNameRawData.Span);
        }
        catch (CryptographicException)
        {
            // Fall back to an ordinal comparison when a value is not a parseable DN.
            return string.Equals(manifestPublisher, SubjectName, StringComparison.Ordinal);
        }

        return DistinguishedNamesEqual(manifestDn, signerDn);
    }

    /// <summary>
    /// Compares two distinguished names by their relative distinguished name (RDN) sequence, matching
    /// each RDN's attribute type (OID) and decoded string value. Comparing decoded values makes the
    /// result independent of the underlying ASN.1 string encoding (e.g. <c>PrintableString</c> vs
    /// <c>UTF8String</c>), which the raw-byte comparison used previously was sensitive to.
    /// </summary>
    private static bool DistinguishedNamesEqual(X500DistinguishedName left, X500DistinguishedName right)
    {
        using IEnumerator<X500RelativeDistinguishedName> l =
            left.EnumerateRelativeDistinguishedNames().GetEnumerator();
        using IEnumerator<X500RelativeDistinguishedName> r =
            right.EnumerateRelativeDistinguishedNames().GetEnumerator();

        while (true)
        {
            bool hasL = l.MoveNext();
            bool hasR = r.MoveNext();
            if (hasL != hasR)
            {
                return false; // Different number of RDNs.
            }

            if (!hasL)
            {
                return true; // Both exhausted with all RDNs equal.
            }

            if (!RelativeDistinguishedNamesEqual(l.Current, r.Current))
            {
                return false;
            }
        }
    }

    private static bool RelativeDistinguishedNamesEqual(X500RelativeDistinguishedName left, X500RelativeDistinguishedName right)
    {
        try
        {
            // Single-valued RDNs (the norm for publisher DNs): compare type + decoded value.
            return string.Equals(
                    left.GetSingleElementType().Value,
                    right.GetSingleElementType().Value,
                    StringComparison.Ordinal)
                && string.Equals(
                    left.GetSingleElementValue(),
                    right.GetSingleElementValue(),
                    StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            // Multi-valued RDN (rare): fall back to comparing this RDN's raw encoding.
            return left.RawData.Span.SequenceEqual(right.RawData.Span);
        }
    }
}
