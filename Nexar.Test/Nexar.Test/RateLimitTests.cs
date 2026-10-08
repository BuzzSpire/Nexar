using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;

namespace Nexar.Test;

public class RateLimitTests
{
    [Fact]
    public async Task ConcurrencyStaysWithinTheLimit()
    {
        var current = 0;
        var peak = 0;
        var handler = new FakeHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref current);
            return FakeHandler.Respond(HttpStatusCode.OK);
        });
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 100 });
        using var client = TestClient.Create(handler, b => b.RateLimit(limiter));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ => { using var r = await client.Get("/").Send(); }));

        Assert.Equal(10, handler.Requests.Count);
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task ThroughputStaysWithinATokenBucket()
    {
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 2,
            TokensPerPeriod = 2,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(200),
            QueueLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.RateLimit(limiter));

        var watch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { using var r = await client.Get("/").Send(); }));

        // 2 immediately, then 2 per 200 ms: at least two replenishments.
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(350), $"Took {watch.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task RefusedPermitIsRateLimitedError()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.RateLimit(limiter));

        using var first = await client.Get("/").Send();
        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsRateLimited);
        Assert.NotNull(ex.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesAlsoTakePermits()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        using var client = TestClient.Create(handler, b => b.RateLimit(limiter).Retry(5, TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsRateLimited);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
