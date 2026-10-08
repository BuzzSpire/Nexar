using System.Net;

namespace Nexar.Test;

public class ProtocolSettingsTests
{
    // ---- #62 HTTP version policy -------------------------------------------------

    [Fact]
    public async Task DefaultsToHttp11()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").Send();

        Assert.Equal(HttpVersion.Version11, handler.Last.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.Last.VersionPolicy);
    }

    [Fact]
    public async Task ClientDefaultVersionAppliesToEveryRequest()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.HttpVersion(HttpVersion.Version20, HttpVersionPolicy.RequestVersionOrHigher));

        await client.Get("/a").Send();
        await client.Post("/b").Send();

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpVersion.Version20, r.Version);
            Assert.Equal(HttpVersionPolicy.RequestVersionOrHigher, r.VersionPolicy);
        });
    }

    [Fact]
    public async Task RequestVersionOverridesClientDefault()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.HttpVersion(HttpVersion.Version20, HttpVersionPolicy.RequestVersionOrHigher));

        await client.Get("/").Version(HttpVersion.Version30, HttpVersionPolicy.RequestVersionExact).Send();
        await client.Get("/").Version(HttpVersion.Version11).Send();

        Assert.Equal(HttpVersion.Version30, handler.Requests[0].Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, handler.Requests[0].VersionPolicy);
        Assert.Equal(HttpVersion.Version11, handler.Requests[1].Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.Requests[1].VersionPolicy);
    }

    [Fact]
    public async Task ResponseReportsTheNegotiatedVersion()
    {
        await using var server = TestServer.Start(_ => ServerResponse.Text("ok"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).HttpVersion(HttpVersion.Version20).Build();

        using var res = await client.Get("/").Send();

        // Plain-text HTTP/2 needs prior knowledge, so the policy falls back to HTTP/1.1.
        Assert.Equal(HttpVersion.Version11, res.Version);
    }

    // ---- #83 Expect: 100-continue --------------------------------------------------

    [Fact]
    public async Task ExpectContinueIsSentOnlyWhenEnabled()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/").Body("x").Send();
        await client.Post("/").Body("x").ExpectContinue().Send();

        Assert.False(handler.Requests[0].Headers.ContainsKey("Expect"));
        Assert.Equal("100-continue", handler.Requests[1].Headers["Expect"]);
    }

    [Fact]
    public async Task ClientDefaultExpectContinueAppliesToBodiesOnly()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.ExpectContinue());

        await client.Get("/").Send();
        await client.Put("/").Body("x").Send();
        await client.Put("/").Body("x").ExpectContinue(false).Send();

        Assert.False(handler.Requests[0].Headers.ContainsKey("Expect"));
        Assert.Equal("100-continue", handler.Requests[1].Headers["Expect"]);
        Assert.False(handler.Requests[2].Headers.ContainsKey("Expect"));
    }

    [Fact]
    public async Task ServerCanRejectBeforeTheBodyIsSent()
    {
        await using var server = TestServer.Start(request => new ServerResponse
        {
            Status = 413,
            Reason = "Payload Too Large",
            RejectBeforeBody = true
        });
        using var client = NexarClient.Builder()
            .BaseUrl(server.Url)
            .ExpectContinueTimeout(TimeSpan.FromSeconds(5))
            .Build();

        using var res = await client.Put("/upload").Body(new byte[1_000_000]).ExpectContinue().Send();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        var request = Assert.Single(server.Requests);
        Assert.Empty(request.Body);
    }

    [Fact]
    public async Task BodyIsSentAfter100Continue()
    {
        await using var server = TestServer.Start(request => ServerResponse.Text($"got {request.Body.Length}"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var text = await client.Put("/upload").Body(new byte[50_000]).ExpectContinue().Send().Text();

        Assert.Equal("got 50000", text);
    }

    // ---- #92 HTTP/2 and HTTP/3 tuning ----------------------------------------------

    [Fact]
    public void Http2AndHttp3SettingsReachTheHandler()
    {
        using var handler = NexarClient.Builder()
            .Http2MultipleConnections()
            .Http2KeepAlive(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
            .Http3MultipleConnections()
            .ExpectContinueTimeout(TimeSpan.FromMilliseconds(500))
            .CreateDefaultHandler();

        Assert.True(handler.EnableMultipleHttp2Connections);
        Assert.True(handler.EnableMultipleHttp3Connections);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), handler.KeepAlivePingTimeout);
        Assert.Equal(HttpKeepAlivePingPolicy.Always, handler.KeepAlivePingPolicy);
        Assert.Equal(TimeSpan.FromMilliseconds(500), handler.Expect100ContinueTimeout);
    }

    [Fact]
    public void InvalidKeepAliveIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().Http2KeepAlive(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    }
}
