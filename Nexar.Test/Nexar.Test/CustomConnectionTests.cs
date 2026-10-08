using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;

namespace Nexar.Test;

public class CustomConnectionTests
{
    private static ServerResponse EchoHost(ServerRequest request) => ServerResponse.Text($"host={request.Header("Host")}");

    // ---- #89 Unix domain sockets and named pipes -------------------------------------

    [Fact]
    public async Task UnixSocket()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexar-{Guid.NewGuid():N}.sock");
        try
        {
            await using var server = TestServer.StartUnix(path, EchoHost);
            using var client = NexarClient.Builder().UnixSocket(path).BaseUrl("http://localhost").Build();

            var text = await client.Get("/containers/json").Send().ErrorForStatus().Text();

            Assert.Equal("host=localhost", text);
            Assert.Equal("/containers/json", Assert.Single(server.Requests).Target);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingUnixSocketIsConnectError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexar-missing-{Guid.NewGuid():N}.sock");
        using var client = NexarClient.Builder().UnixSocket(path).BaseUrl("http://localhost").Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect, $"Expected Connect, got {ex.Kind}: {ex.Message}");
    }

    [Fact]
    public async Task NamedPipe()
    {
        var pipeName = $"nexar-{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<ServerRequest>();
        var serverTask = Task.Run(async () =>
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            await TestServer.ServeAsync(pipe, null, EchoHost, requests);
        });
        using var client = NexarClient.Builder().NamedPipe(pipeName).BaseUrl("http://localhost").Build();

        var text = await client.Get("/_ping").Send().ErrorForStatus().Text();
        await serverTask;

        Assert.Equal("host=localhost", text);
        Assert.Equal("/_ping", Assert.Single(requests).Target);
    }

    // ---- #93 DNS overrides and local address binding ---------------------------------

    [Fact]
    public async Task ResolveSkipsDnsAndKeepsTheHostHeader()
    {
        await using var server = TestServer.Start(EchoHost);
        using var client = NexarClient.Builder().Resolve("api.example.test", IPAddress.Loopback).Build();

        var text = await client.Get($"http://api.example.test:{server.Port}/").Send().ErrorForStatus().Text();

        Assert.Equal($"host=api.example.test:{server.Port}", text);
    }

    [Fact]
    public async Task ResolveKeepsTlsHostNameValidation()
    {
        using var authority = TestCertificates.CreateAuthority();
        using var certificate = TestCertificates.CreateLeaf("secure.example.test", authority, serverAuth: true);
        await using var server = TestServer.StartTls(certificate, EchoHost);
        var loopback = System.Net.Sockets.Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;

        using var valid = NexarClient.Builder()
            .Resolve("secure.example.test", loopback)
            .AddRootCertificate(authority)
            .Build();
        using var wrongName = NexarClient.Builder()
            .Resolve("other.example.test", loopback)
            .AddRootCertificate(authority)
            .Build();

        var text = await valid.Get($"https://secure.example.test:{server.Port}/").Send().ErrorForStatus().Text();
        var ex = await Assert.ThrowsAsync<NexarException>(() => wrongName.Get($"https://other.example.test:{server.Port}/").Send());

        Assert.Equal($"host=secure.example.test:{server.Port}", text);
        Assert.True(ex.IsConnect);
    }

    [Fact]
    public async Task OtherHostsStillUseDns()
    {
        await using var server = TestServer.Start(EchoHost);
        using var client = NexarClient.Builder().Resolve("api.example.test", IPAddress.Parse("10.255.255.1")).Build();

        var text = await client.Get($"http://127.0.0.1:{server.Port}/").Send().Text();

        Assert.Equal($"host=127.0.0.1:{server.Port}", text);
    }

    [Fact]
    public async Task LocalAddress()
    {
        await using var server = TestServer.Start(EchoHost);
        using var client = NexarClient.Builder().LocalAddress(IPAddress.Loopback).Build();

        using var res = await client.Get($"{server.Url}/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public void UnixSocketCannotBeCombinedWithResolve()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .UnixSocket("/tmp/x.sock")
            .Resolve("a.test", IPAddress.Loopback)
            .Build());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public void ResolveNeedsAnAddress()
    {
        Assert.Throws<ArgumentException>(() => NexarClient.Builder().Resolve("a.test"));
    }
}
