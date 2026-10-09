using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Nexar.Test;

public sealed class PinningTests : IDisposable
{
    private readonly X509Certificate2 _authority = TestCertificates.CreateAuthority();
    private readonly X509Certificate2 _leaf;

    public PinningTests()
    {
        _leaf = TestCertificates.CreateLeaf("localhost", _authority, serverAuth: true);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _leaf.Dispose();
    }

    private TestServer Server() => TestServer.StartTls(_leaf, _ => ServerResponse.Text("ok"));

    [Fact]
    public async Task MatchingLeafPinPasses()
    {
        await using var server = Server();
        using var client = NexarClient.Builder()
            .AddRootCertificate(_authority)
            .PinCertificate("localhost", ClientBuilder.ComputePin(_leaf))
            .Build();

        Assert.Equal("ok", await client.Get($"{server.Url}/").Send().Text());
    }

    [Fact]
    public async Task MatchingAuthorityPinPasses()
    {
        await using var server = Server();
        using var client = NexarClient.Builder()
            .AddRootCertificate(_authority)
            .PinCertificate("localhost", ClientBuilder.ComputePin(_authority))
            .Build();

        Assert.Equal("ok", await client.Get($"{server.Url}/").Send().Text());
    }

    [Fact]
    public async Task MismatchFailsEvenWhenTheChainIsTrusted()
    {
        using var other = TestCertificates.CreateServerCertificate();
        await using var server = Server();
        using var client = NexarClient.Builder()
            .AddRootCertificate(_authority)
            .PinCertificate("localhost", ClientBuilder.ComputePin(other))
            .Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get($"{server.Url}/").Send());

        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task BackupPinAllowsRotation()
    {
        using var old = TestCertificates.CreateServerCertificate();
        await using var server = Server();
        using var client = NexarClient.Builder()
            .AddRootCertificate(_authority)
            .PinCertificate("localhost", ClientBuilder.ComputePin(old), ClientBuilder.ComputePin(_leaf))
            .Build();

        Assert.Equal("ok", await client.Get($"{server.Url}/").Send().Text());
    }

    [Fact]
    public async Task OtherHostsAreUnaffected()
    {
        await using var server = Server();
        using var client = NexarClient.Builder()
            .AddRootCertificate(_authority)
            .PinCertificate("api.bank.example", "sha256/" + Convert.ToBase64String(new byte[32]))
            .Build();

        Assert.Equal("ok", await client.Get($"{server.Url}/").Send().Text());
    }

    [Fact]
    public async Task PinsApplyEvenWithDangerAcceptInvalidCerts()
    {
        using var other = TestCertificates.CreateServerCertificate();
        await using var server = Server();
        using var client = NexarClient.Builder()
            .DangerAcceptInvalidCerts()
            .PinCertificate("localhost", ClientBuilder.ComputePin(other))
            .Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get($"{server.Url}/").Send());

        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task PinningDoesNotReplaceNormalValidation()
    {
        await using var server = Server();
        using var client = NexarClient.Builder()   // no AddRootCertificate: the chain is untrusted
            .PinCertificate("localhost", ClientBuilder.ComputePin(_leaf))
            .Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get($"{server.Url}/").Send());

        Assert.True(ex.IsConnect);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("sha1/AAAA")]
    [InlineData("sha256/not base64!")]
    [InlineData("sha256/AAAA")]
    public void InvalidPinsAreRejected(string pin)
    {
        Assert.Throws<ArgumentException>(() => NexarClient.Builder().PinCertificate("host", pin));
    }

    [Fact]
    public void ComputePinFormat()
    {
        var pin = ClientBuilder.ComputePin(_leaf);

        Assert.StartsWith("sha256/", pin);
        Assert.Equal(32, Convert.FromBase64String(pin["sha256/".Length..]).Length);
    }
}
