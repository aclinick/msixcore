using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MsixCore.Packaging.Integrity;

namespace MsixCore.Packaging.Tests;

public class PackageSignatureReaderTests
{
    private static readonly byte[] Magic = "PKCX"u8.ToArray();

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static byte[] BuildSignature(X509Certificate2 certificate, bool prependMagic)
    {
        var content = new ContentInfo(Encoding.UTF8.GetBytes("appx-indirect-data"));
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(certificate) { IncludeOption = X509IncludeOption.EndCertOnly };
        cms.ComputeSignature(signer);

        byte[] der = cms.Encode();
        if (!prependMagic)
        {
            return der;
        }

        byte[] withMagic = new byte[Magic.Length + der.Length];
        Magic.CopyTo(withMagic, 0);
        der.CopyTo(withMagic, Magic.Length);
        return withMagic;
    }

    [Fact]
    public void Read_ExtractsSignerIdentity_AndValidatesSignature()
    {
        using X509Certificate2 cert = CreateCertificate("CN=Contoso Corporation, O=Contoso");
        byte[] signature = BuildSignature(cert, prependMagic: true);

        PackageSignature result = PackageSignatureReader.Read(signature);

        Assert.Equal(cert.Thumbprint, result.Thumbprint);
        Assert.True(result.IsSignatureValid);
        Assert.Equal(cert.NotAfter, result.NotAfter);
    }

    [Fact]
    public void Read_WithoutMagic_StillDecodes()
    {
        using X509Certificate2 cert = CreateCertificate("CN=Contoso");
        byte[] signature = BuildSignature(cert, prependMagic: false);

        PackageSignature result = PackageSignatureReader.Read(signature);

        Assert.True(result.IsSignatureValid);
    }

    [Fact]
    public void Read_FromStream_Works()
    {
        using X509Certificate2 cert = CreateCertificate("CN=Contoso");
        byte[] signature = BuildSignature(cert, prependMagic: true);

        PackageSignature result = PackageSignatureReader.Read(new MemoryStream(signature));

        Assert.True(result.IsSignatureValid);
    }

    [Fact]
    public void MatchesPublisher_EquivalentDistinguishedName_ReturnsTrue()
    {
        using X509Certificate2 cert = CreateCertificate("CN=Contoso Corporation, O=Contoso");
        PackageSignature result = PackageSignatureReader.Read(BuildSignature(cert, prependMagic: true));

        Assert.True(result.MatchesPublisher("CN=Contoso Corporation, O=Contoso"));
    }

    [Fact]
    public void MatchesPublisher_DifferentPublisher_ReturnsFalse()
    {
        using X509Certificate2 cert = CreateCertificate("CN=Contoso Corporation, O=Contoso");
        PackageSignature result = PackageSignatureReader.Read(BuildSignature(cert, prependMagic: true));

        Assert.False(result.MatchesPublisher("CN=Fabrikam"));
    }

    [Fact]
    public void Read_MalformedBytes_Throws()
    {
        byte[] garbage = [1, 2, 3, 4, 5, 6, 7, 8];
        Assert.Throws<InvalidDataException>(() => PackageSignatureReader.Read(garbage));
    }

    [Fact]
    public void Read_MagicOnly_Throws()
    {
        Assert.Throws<InvalidDataException>(() => PackageSignatureReader.Read(Magic));
    }
}
