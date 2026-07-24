using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// Reads an <c>AppxSignature.p7x</c> and extracts the primary signer identity and CMS integrity
/// status. Cross-platform: uses <see cref="SignedCms"/>, which is backed by OpenSSL on Linux.
/// </summary>
public static class PackageSignatureReader
{
    /// <summary>The 4-byte magic ("PKCX") that prefixes the DER PKCS#7 content in a <c>.p7x</c> file.</summary>
    private static ReadOnlySpan<byte> P7xMagic => "PKCX"u8;

    /// <summary>Reads and validates the signer information from a signature stream.</summary>
    /// <param name="signatureStream">A readable stream over an <c>AppxSignature.p7x</c> part.</param>
    /// <returns>The extracted <see cref="PackageSignature"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signatureStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The signature is malformed or has no signer.</exception>
    public static PackageSignature Read(Stream signatureStream)
    {
        ArgumentNullException.ThrowIfNull(signatureStream);

        using var buffer = new MemoryStream();
        signatureStream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    /// <summary>Reads and validates the signer information from raw signature bytes.</summary>
    /// <param name="signatureBytes">The full <c>AppxSignature.p7x</c> bytes (with or without the PKCX magic).</param>
    /// <returns>The extracted <see cref="PackageSignature"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signatureBytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The signature is malformed or has no signer.</exception>
    public static PackageSignature Read(byte[] signatureBytes)
    {
        ArgumentNullException.ThrowIfNull(signatureBytes);

        ReadOnlyMemory<byte> der = StripMagic(signatureBytes);

        var cms = new SignedCms();
        try
        {
            cms.Decode(der.Span);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("The signature is not a valid PKCS#7/CMS structure.", ex);
        }

        if (cms.SignerInfos.Count == 0)
        {
            throw new InvalidDataException("The signature contains no signer information.");
        }

        SignerInfo signer = cms.SignerInfos[0];
        X509Certificate2 certificate = signer.Certificate
            ?? throw new InvalidDataException("The signature does not embed the signer certificate.");

        bool signatureValid;
        try
        {
            // verifySignatureOnly: check the CMS digest/signature integrity without building a trust chain.
            cms.CheckSignature(verifySignatureOnly: true);
            signatureValid = true;
        }
        catch (CryptographicException)
        {
            signatureValid = false;
        }

        return new PackageSignature
        {
            SubjectName = certificate.SubjectName.Name,
            IssuerName = certificate.IssuerName.Name,
            Thumbprint = certificate.Thumbprint,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            IsCmsIntegrityValid = signatureValid,
        };
    }

    private static ReadOnlyMemory<byte> StripMagic(byte[] signatureBytes)
    {
        if (signatureBytes.Length < P7xMagic.Length || !signatureBytes.AsSpan(0, P7xMagic.Length).SequenceEqual(P7xMagic))
        {
            throw new InvalidDataException(
                "The signature is missing the required 'PKCX' file identifier of an AppxSignature.p7x part.");
        }

        return signatureBytes.AsMemory(P7xMagic.Length);
    }
}
