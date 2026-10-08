using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nexar.Test;

/// <summary>
/// Regression tests for #49 – #54.
/// </summary>
public class BugFixTests
{
    // ---- #49 Retry must not resend non-idempotent requests by default ----------

    [Fact]
    public async Task PostIsNotRetriedOnTransientStatus()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Post("/orders").Json(new { id = 1 }).Send();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostIsNotRetriedOnTimeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero).Timeout(TimeSpan.FromMilliseconds(30)));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Patch("/orders/1").Json(new { }).Send());

        Assert.True(ex.IsTimeout);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryableOptsPostIn()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Created });
        var handler = new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue()));
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Post("/orders").Header("Idempotency-Key", "k1").Json(new { id = 1 }).Retryable().Send();

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryableFalseOptsGetOut()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Get("/").Retryable(false).Send();

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task IdempotentMethodsAreRetried(string method)
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(2, TimeSpan.Zero));

        using var res = await client.Request(new HttpMethod(method), "/").Send();

        Assert.Equal(3, handler.Requests.Count);
    }

    // ---- #50 Honor Retry-After --------------------------------------------------

    private static HttpResponseMessage WithRetryAfter(HttpStatusCode status, RetryConditionHeaderValue retryAfter)
    {
        var response = FakeHandler.Respond(status);
        response.Headers.RetryAfter = retryAfter;
        return response;
    }

    [Fact]
    public async Task RetryAfterSecondsReplacesBackoff()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            WithRetryAfter(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(1))),
            FakeHandler.Respond(HttpStatusCode.OK)
        });
        var handler = new FakeHandler(_ => responses.Dequeue());
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero));

        var started = DateTime.UtcNow;
        using var res = await client.Get("/").Send();
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900), $"Elapsed {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RetryAfterDateInThePastRetriesImmediately()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            WithRetryAfter(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1))),
            FakeHandler.Respond(HttpStatusCode.OK)
        });
        var handler = new FakeHandler(_ => responses.Dequeue());
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.FromSeconds(10)));

        var started = DateTime.UtcNow;
        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RetryAfterAboveMaxDelayReturnsTheResponse()
    {
        var handler = new FakeHandler(_ =>
            WithRetryAfter(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromMinutes(5))));
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.Zero, maxDelay: TimeSpan.FromSeconds(30)));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), res.Headers.RetryAfter!.Delta);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MaxDelayCapsExponentialBackoff()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.Retry(3, TimeSpan.FromMilliseconds(100), maxDelay: TimeSpan.FromMilliseconds(100)));

        var started = DateTime.UtcNow;
        using var res = await client.Get("/").Send();
        var elapsed = DateTime.UtcNow - started;

        // Uncapped this would be 100 + 200 + 400 = 700 ms.
        Assert.Equal(4, handler.Requests.Count);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(650), $"Elapsed {elapsed.TotalMilliseconds}ms");
    }

    // ---- #51 ErrorForStatus keeps the error body ---------------------------------

    private record Problem(string Type, string Title, int Status, string Detail);

    [Fact]
    public async Task ErrorForStatusKeepsBodyAndHeaders()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.UnprocessableEntity,
                "{\"type\":\"https://example.com/invalid\",\"title\":\"Invalid\",\"status\":422,\"detail\":\"Name is required\"}",
                "application/problem+json");
            response.Headers.Add("X-Request-Id", "abc");
            return response;
        });
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Post("/users").Send().ErrorForStatus());

        Assert.Contains("Name is required", ex.ResponseBody);
        Assert.False(ex.IsResponseBodyTruncated);
        Assert.Equal("abc", ex.ResponseHeaders!["x-request-id"].Single());
        Assert.StartsWith("application/problem+json", ex.ResponseHeaders["Content-Type"].Single());
        Assert.Equal(new Problem("https://example.com/invalid", "Invalid", 422, "Name is required"), ex.Json<Problem>());
    }

    [Fact]
    public async Task ErrorForStatusTruncatesLargeBodies()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, new string('x', 100_000), "text/plain");
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().ErrorForStatus());

        Assert.True(ex.IsResponseBodyTruncated);
        Assert.Equal(NexarResponse.MaxErrorBodyBytes, ex.ResponseBody!.Length);
        Assert.Null(ex.Json<Problem>());
    }

    [Fact]
    public async Task ErrorForStatusJsonReturnsDefaultForNonJsonBodies()
    {
        var handler = new FakeHandler(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().ErrorForStatus());

        Assert.Equal("<html>bad gateway</html>", ex.ResponseBody);
        Assert.Null(ex.Json<Problem>());
    }

    [Fact]
    public async Task ErrorForStatusUsesTheResponseCharset()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("çğış", Encoding.Unicode, "text/plain")
        });
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().ErrorForStatus());

        Assert.Equal("çğış", ex.ResponseBody);
    }

    // ---- #52 Timeout covers reading the body -------------------------------------

    /// <summary>Sends headers right away, then never finishes the body.</summary>
    private sealed class StallingContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync("partial"u8.ToArray(), cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task StalledBodyTimesOut()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() });
        using var client = TestClient.Create(handler, b => b.Timeout(TimeSpan.FromMilliseconds(100)));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().Text());

        Assert.True(ex.IsTimeout);
    }

    [Fact]
    public async Task CallerCancellationWhileReadingBodyIsNotWrapped()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() });
        using var client = TestClient.Create(handler, b => b.Timeout(TimeSpan.FromHours(1)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Get("/").Send().Text(cts.Token));
    }

    [Fact]
    public async Task BufferedBodyCanBeReadAfterTheDeadline()
    {
        var handler = new FakeHandler(body: "{\"a\":1}");
        using var client = TestClient.Create(handler, b => b.Timeout(TimeSpan.FromMilliseconds(100)));

        using var res = await client.Get("/").Send();
        await res.Text();
        await Task.Delay(200);

        Assert.Equal("{\"a\":1}", await res.Text());
    }

    // ---- #53 Automatic decompression ---------------------------------------------

    [Fact]
    public void DefaultHandlerDecompressesEverything()
    {
        using var handler = NexarClient.Builder().CreateDefaultHandler();

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }

    [Fact]
    public void DecompressionCanBeTurnedOff()
    {
        using var handler = NexarClient.Builder().Decompression(DecompressionMethods.None).CreateDefaultHandler();

        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
    }

    [Fact]
    public void DecompressionCannotBeCombinedWithCustomHandler()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .HttpMessageHandler(new FakeHandler())
            .Decompression(DecompressionMethods.GZip)
            .Build());

        Assert.True(ex.IsBuilder);
    }

    // ---- #54 Query goes before the fragment --------------------------------------

    [Theory]
    [InlineData("/page#section", "https://api.test/page?b=2#section")]
    [InlineData("/page?a=1#section", "https://api.test/page?a=1&b=2#section")]
    [InlineData("https://other.test/x#top", "https://other.test/x?b=2#top")]
    public async Task QueryIsInsertedBeforeFragment(string url, string expected)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get(url).Query("b", 2).Send();

        Assert.Equal(expected, handler.Last.Url.AbsoluteUri);
    }
}
