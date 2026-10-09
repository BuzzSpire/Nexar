using System.Diagnostics;
using System.Net;
using Nexar.Testing;

namespace Nexar.Test;

public class RetryPolicyTests
{
    [Fact]
    public async Task JitterSpreadsTheDelays()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable);
        var delays = new List<TimeSpan>();
        using var client = mock.CreateClient(configure: b => b
            .Retry(20, TimeSpan.FromMilliseconds(10), exponentialBackoff: false, jitter: true)
            .OnRetry(e => delays.Add(e.Delay)));

        using var res = await client.Get("/").Send();

        Assert.Equal(20, delays.Count);
        Assert.All(delays, d => Assert.InRange(d, TimeSpan.Zero, TimeSpan.FromMilliseconds(10)));
        Assert.True(delays.Distinct().Count() > 1, "jittered delays should differ");
    }

    [Fact]
    public async Task WithoutJitterDelaysAreExact()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable);
        var delays = new List<TimeSpan>();
        using var client = mock.CreateClient(configure: b => b.Retry(3, TimeSpan.FromMilliseconds(5)).OnRetry(e => delays.Add(e.Delay)));

        using var res = await client.Get("/").Send();

        Assert.Equal(new[] { 5.0, 10.0, 20.0 }, delays.Select(d => d.TotalMilliseconds));
    }

    [Fact]
    public async Task RetryWhenAddsAStatus()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.Conflict).Respond(HttpStatusCode.OK).Times(2);
        using var client = mock.CreateClient(configure: b => b
            .Retry(2, TimeSpan.Zero)
            .RetryWhen(ctx => ctx.Response?.StatusCode == HttpStatusCode.Conflict ? true : null));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task RetryWhenVetoesADefault()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable).Times(1);
        using var client = mock.CreateClient(configure: b => b
            .Retry(3, TimeSpan.Zero)
            .RetryWhen(ctx => ctx.Response?.StatusCode == HttpStatusCode.ServiceUnavailable ? false : null));

        using var res = await client.Get("/").Send();

        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task RetryWhenSeesTheContext()
    {
        var mock = new MockHttp();
        mock.OnGet("/x").Throws(new HttpRequestException(HttpRequestError.InvalidResponse, "garbage")).Respond(HttpStatusCode.OK);
        var seen = new List<RetryContext>();
        using var client = mock.CreateClient(configure: b => b
            .Retry(1, TimeSpan.Zero)
            .RetryWhen(ctx => { seen.Add(ctx); return null; }));

        using var res = await client.Get("/x").Send();

        var context = Assert.Single(seen);
        Assert.Equal(1, context.Attempt);
        Assert.Equal(HttpMethod.Get, context.Method);
        Assert.EndsWith("/x", context.Url.AbsoluteUri);
        Assert.IsType<HttpRequestException>(context.Exception);
        Assert.Null(context.Response);
        Assert.True(context.RetriedByDefault);
    }

    [Fact]
    public async Task RetryWhenCannotForceANonIdempotentRetry()
    {
        var mock = new MockHttp();
        mock.OnPost("/orders").Respond(HttpStatusCode.ServiceUnavailable).Times(1);
        using var client = mock.CreateClient(configure: b => b.Retry(3, TimeSpan.Zero).RetryWhen(_ => true));

        using var res = await client.Post("/orders").Json(new { id = 1 }).Send();

        mock.VerifyAllCalled();
    }

    [Fact]
    public async Task IdempotencyKeyMakesAPostRetryableWithTheSameKey()
    {
        var mock = new MockHttp();
        mock.OnPost("/payments").Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.Created);
        using var client = mock.CreateClient(configure: b => b.Retry(2, TimeSpan.Zero));

        using var res = await client.Post("/payments").Json(new { amount = 10 }).IdempotencyKey().Send();

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var keys = mock.Requests.Select(r => r.Header("Idempotency-Key")).ToList();
        Assert.Equal(2, keys.Count);
        Assert.True(Guid.TryParse(keys[0], out _));
        Assert.Equal(keys[0], keys[1]);
    }

    [Fact]
    public async Task IdempotencyKeyWithCustomValueAndHeader()
    {
        var mock = new MockHttp();
        mock.OnPost("/").Respond();
        using var client = mock.CreateClient();

        await client.Post("/").IdempotencyKey("order-42", "X-Request-Key").Send();

        Assert.Equal("order-42", mock.Requests[0].Header("X-Request-Key"));
    }

    [Fact]
    public async Task OnRetryReportsEveryResendIncludingReauthentication()
    {
        var mock = new MockHttp();
        mock.OnGet("/").WithHeader("Authorization", "Bearer token-2").Respond(HttpStatusCode.OK);
        mock.OnGet("/").Respond(HttpStatusCode.Unauthorized);
        var version = 1;
        var events = new List<RetryEvent>();
        using var client = mock.CreateClient(configure: b => b
            .Auth(Auth.Bearer((force, _) => ValueTask.FromResult($"token-{(force ? ++version : version)}")))
            .OnRetry(events.Add));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var e = Assert.Single(events);
        Assert.Equal("unauthorized", e.Reason);
        Assert.Equal(1, e.Attempt);
    }

    [Fact]
    public async Task FailingOnRetryCallbackDoesNotBreakTheRequest()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK);
        using var client = mock.CreateClient(configure: b => b.Retry(1, TimeSpan.Zero).OnRetry(_ => throw new InvalidOperationException("boom")));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task RetryWhenBeforeRetryIsKept()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.Conflict).Respond(HttpStatusCode.OK);
        using var client = mock.CreateClient(configure: b => b
            .RetryWhen(ctx => ctx.Response?.StatusCode == HttpStatusCode.Conflict ? true : null)
            .Retry(1, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
