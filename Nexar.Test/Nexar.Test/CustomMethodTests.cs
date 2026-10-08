using System.Net;

namespace Nexar.Test;

public class CustomMethodTests
{
    [Theory]
    [InlineData("PROPFIND")]
    [InlineData("MKCOL")]
    [InlineData("LINK")]
    public async Task MethodByName(string method)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Request(method, "/dav/folder").Header("Depth", "1").Send();

        Assert.Equal(method, handler.Last.Method.Method);
    }

    [Fact]
    public async Task Shortcuts()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Options("/").Send();
        await client.Trace("/").Send();
        await client.Query("/search").Json(new { q = "nexar" }).Send();

        Assert.Equal(new[] { "OPTIONS", "TRACE", "QUERY" }, handler.Requests.Select(r => r.Method.Method));
        Assert.Equal("{\"q\":\"nexar\"}", handler.Requests[2].Body);
    }

    [Theory]
    [InlineData("GET ME")]
    [InlineData("PUT\r\n")]
    [InlineData("")]
    public async Task InvalidMethodIsBuilderError(string method)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Request(method, "/").Send());

        Assert.True(ex.IsBuilder);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task QueryIsRetriedLikeOtherSafeMethods()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero));

        using var res = await client.Query("/search").Json(new { q = 1 }).Send();

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UnknownMethodsAreNotRetried()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Request("PROPPATCH", "/").Send();

        Assert.Single(handler.Requests);
    }
}
