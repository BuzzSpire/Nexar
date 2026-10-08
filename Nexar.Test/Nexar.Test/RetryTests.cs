using System.Net;
using System.Text;
using Nexar.Testing;

namespace Nexar.Test;

/// <summary>
/// Written with <see cref="MockHttp"/> from Nexar.Testing, so the companion package is exercised by the suite.
/// </summary>
public class RetryTests
{
    private static NexarClient Client(MockHttp mock, int retries = 0, TimeSpan? delay = null) =>
        mock.CreateClient(configure: b =>
        {
            if (retries > 0)
            {
                b.Retry(retries, delay ?? TimeSpan.Zero);
            }
        });

    [Fact]
    public async Task DoesNotRetryByDefault()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable);
        using var client = Client(mock);

        using var res = await client.Get("/").Send();

        Assert.Single(mock.Requests);
    }

    [Fact]
    public async Task RetriesTransientStatusUntilSuccess()
    {
        var mock = new MockHttp();
        mock.OnGet("/")
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.OK)
            .Times(3);
        using var client = Client(mock, retries: 3);

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task ReturnsLastResponseWhenRetriesAreExhausted()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.BadGateway).Times(3);
        using var client = Client(mock, retries: 2);

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task DoesNotRetryNonTransientStatus()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.NotFound).Times(1);
        using var client = Client(mock, retries: 3);

        using var res = await client.Get("/").Send();

        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task RetriesConnectionErrorsAndResendsTheBody()
    {
        var refused = new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var mock = new MockHttp();
        mock.OnPost("/").Throws(refused).Throws(refused).Respond(HttpStatusCode.OK);
        using var client = Client(mock, retries: 3);

        using var res = await client.Post("/").Json(new { a = 1 }).Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, mock.Requests.Count);
        Assert.All(mock.Requests, r => Assert.Equal("{\"a\":1}", r.Body));
    }

    [Fact]
    public async Task ThrowsAfterExhaustingRetriesOnConnectionErrors()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Throws(new HttpRequestException(HttpRequestError.ConnectionError, "refused")).Times(3);
        using var client = Client(mock, retries: 2);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsConnect);
        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task NeverRetriesStreamBodies()
    {
        var mock = new MockHttp();
        mock.OnPut("/").Respond(HttpStatusCode.ServiceUnavailable).Times(1);
        using var client = Client(mock, retries: 3);

        using var res = await client.Put("/").Body(new MemoryStream(Encoding.UTF8.GetBytes("x"))).Send();

        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task UsesExponentialBackoff()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable);
        using var client = Client(mock, retries: 3, delay: TimeSpan.FromMilliseconds(40));

        var started = DateTime.UtcNow;
        using var res = await client.Get("/").Send();
        var elapsed = DateTime.UtcNow - started;

        // 40 + 80 + 160 ms
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(260), $"Elapsed {elapsed.TotalMilliseconds}ms");
    }
}
