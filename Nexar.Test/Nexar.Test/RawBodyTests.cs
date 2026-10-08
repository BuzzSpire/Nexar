using System.Net;
using System.Text;

namespace Nexar.Test;

public class RawBodyTests
{
    [Fact]
    public async Task ReadOnlyMemoryBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);
        var buffer = Encoding.UTF8.GetBytes("xxhelloxx");

        await client.Post("/").Body(buffer.AsMemory(2, 5), "text/plain").Send();

        Assert.Equal("hello", handler.Last.Body);
        Assert.Equal("text/plain", handler.Last.ContentHeaders["Content-Type"]);
        Assert.Equal("5", handler.Last.ContentHeaders["Content-Length"]);
    }

    [Fact]
    public async Task FactoryBodyIsCreatedForEveryAttempt()
    {
        var created = 0;
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK });
        var handler = new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue()));
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero));

        using var res = await client.Put("/").Body(() => new StringContent($"attempt {++created}")).Send();

        Assert.Equal(2, created);
        Assert.Equal(new[] { "attempt 1", "attempt 2" }, handler.Requests.Select(r => r.Body));
    }

    [Fact]
    public async Task NonReplayableFactoryIsNotRetried()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Put("/").Body(() => new StringContent("once"), replayable: false).Send();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReadyMadeContentIsSentOnce()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));
        var content = new StringContent("<x/>", Encoding.UTF8, "application/xml");

        using var res = await client.Put("/").Body(content).Send();

        Assert.Single(handler.Requests);
        Assert.Equal("<x/>", handler.Last.Body);
        Assert.StartsWith("application/xml", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task TrailersAreExposed()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.OK, "body");
            response.TrailingHeaders.Add("grpc-status", "0");
            response.TrailingHeaders.Add("X-Checksum", "abc123");
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();
        await res.Text();

        Assert.Equal("0", res.Trailers.GetValues("grpc-status").Single());
        Assert.Equal("abc123", res.Trailers.GetValues("X-Checksum").Single());
    }
}
