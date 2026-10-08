using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Nexar.Test;

/// <summary>
/// Throwaway certificates for TLS tests: a CA, a "localhost" server certificate and a client certificate.
/// </summary>
public static class TestCertificates
{
    /// <summary>A self-signed "localhost" certificate.</summary>
    public static X509Certificate2 CreateServerCertificate() => CreateLeaf("localhost", issuer: null, serverAuth: true);

    public static X509Certificate2 CreateAuthority(string name = "Nexar Test CA")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        // Outlives every leaf it signs (leaves expire after 30 days).
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(60));
        return Reload(certificate);
    }

    /// <summary>A certificate for <paramref name="name"/>, signed by <paramref name="issuer"/> (self-signed if null).</summary>
    public static X509Certificate2 CreateLeaf(string name, X509Certificate2? issuer, bool serverAuth)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [serverAuth ? new Oid("1.3.6.1.5.5.7.3.1") : new Oid("1.3.6.1.5.5.7.3.2")], false));
        if (serverAuth)
        {
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(name);
            request.CertificateExtensions.Add(san.Build());
        }

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        if (issuer == null)
        {
            using var selfSigned = request.CreateSelfSigned(notBefore, notAfter);
            return Reload(selfSigned);
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        using var signed = request.Create(issuer, notBefore, notAfter, serial);
        using var withKey = signed.CopyWithPrivateKey(key);
        return Reload(withKey);
    }

    /// <summary>
    /// Round-trips through PKCS#12 so the private key is usable by SslStream on every platform
    /// (Windows rejects ephemeral keys for server authentication).
    /// </summary>
    private static X509Certificate2 Reload(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
}
