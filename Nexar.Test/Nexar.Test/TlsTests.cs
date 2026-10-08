using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Nexar.Test;

public sealed class TlsTests : IDisposable
{
    private readonly X509Certificate2 _authority = TestCertificates.CreateAuthority();
    private readonly X509Certificate2 _serverCertificate;

    public TlsTests()
    {
        _serverCertificate = TestCertificates.CreateLeaf("localhost", _authority, serverAuth: true);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _serverCertificate.Dispose();
    }

    private static ServerResponse Echo(ServerRequest request) =>
        ServerResponse.Text(request.ClientCertificate?.Subject ?? "no client certificate");

    [Fact]
    public async Task UntrustedServerCertificateFails()
    {
        await using var server = TestServer.StartTls(_serverCertificate, Echo);
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task AddedRootCertificateIsTrusted()
    {
        await using var server = TestServer.StartTls(_serverCertificate, Echo);
        using var client = NexarClient.Builder().BaseUrl(server.Url).AddRootCertificate(_authority).Build();

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task HostNameIsStillChecked()
    {
        using var wrongHost = TestCertificates.CreateLeaf("other.test", _authority, serverAuth: true);
        await using var server = TestServer.StartTls(wrongHost, Echo);
        using var client = NexarClient.Builder().BaseUrl(server.Url).AddRootCertificate(_authority).Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task CertificateFromAnotherAuthorityIsRejected()
    {
        using var otherAuthority = TestCertificates.CreateAuthority("Other CA");
        await using var server = TestServer.StartTls(_serverCertificate, Echo);
        using var client = NexarClient.Builder().BaseUrl(server.Url).AddRootCertificate(otherAuthority).Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task ClientCertificateIsPresented()
    {
        using var clientCertificate = TestCertificates.CreateLeaf("nexar-client", _authority, serverAuth: false);
        await using var server = TestServer.StartTls(_serverCertificate, Echo, requireClientCertificate: true);
        using var client = NexarClient.Builder()
            .BaseUrl(server.Url)
            .AddRootCertificate(_authority)
            .ClientCertificate(clientCertificate)
            .Build();

        var subject = await client.Get("/").Send().ErrorForStatus().Text();

        Assert.Equal("CN=nexar-client", subject);
    }

    [Fact]
    public void ClientCertificateNeedsPrivateKey()
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(_serverCertificate.Export(X509ContentType.Cert));

        Assert.Throws<ArgumentException>(() => NexarClient.Builder().ClientCertificate(publicOnly));
    }

    [Theory]
    [InlineData(SslProtocols.Tls12, SslProtocols.Tls12 | SslProtocols.Tls13)]
    [InlineData(SslProtocols.Tls13, SslProtocols.Tls13)]
    public void MinTlsVersionReachesTheHandler(SslProtocols minimum, SslProtocols expected)
    {
        using var handler = NexarClient.Builder().MinTlsVersion(minimum).CreateDefaultHandler();

        Assert.Equal(expected, handler.SslOptions.EnabledSslProtocols);
    }

    [Fact]
    public void OldTlsVersionsAreRejected()
    {
#pragma warning disable SYSLIB0039 // TLS 1.0/1.1 are obsolete; that is the point of the test
        Assert.Throws<ArgumentException>(() => NexarClient.Builder().MinTlsVersion(SslProtocols.Tls11));
#pragma warning restore SYSLIB0039
    }
}
