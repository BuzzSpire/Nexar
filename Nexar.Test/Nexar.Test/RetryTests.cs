using System.Net;
using System.Text;

namespace Nexar.Test;

public class RetryTests
{
    [Fact]
    public async Task DoesNotRetryByDefault()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesTransientStatusUntilSuccess()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests, HttpStatusCode.OK });
        var handler = new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue()));
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ReturnsLastResponseWhenRetriesAreExhausted()
    {
        var handler = new FakeHandler(HttpStatusCode.BadGateway);
        using var client = TestClient.Create(handler, b => b.Retry(2, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryNonTransientStatus()
    {
        var handler = new FakeHandler(HttpStatusCode.NotFound);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesConnectionErrorsAndResendsTheBody()
    {
        var attempts = 0;
        var handler = new FakeHandler(_ => ++attempts < 3
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : FakeHandler.Respond(HttpStatusCode.OK));
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Post("/").Json(new { a = 1 }).Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("{\"a\":1}", r.Body));
    }

    [Fact]
    public async Task ThrowsAfterExhaustingRetriesOnConnectionErrors()
    {
        var handler = new FakeHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using var client = TestClient.Create(handler, b => b.Retry(2, TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task NeverRetriesStreamBodies()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Put("/").Body(new MemoryStream(Encoding.UTF8.GetBytes("x"))).Send();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UsesExponentialBackoff()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.FromMilliseconds(40)));

        var started = DateTime.UtcNow;
        using var res = await client.Get("/").Send();
        var elapsed = DateTime.UtcNow - started;

        // 40 + 80 + 160 ms
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(260), $"Elapsed {elapsed.TotalMilliseconds}ms");
    }
}
