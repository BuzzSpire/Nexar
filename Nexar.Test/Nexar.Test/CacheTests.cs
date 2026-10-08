using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nexar.Test;

public class CacheTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// A server whose responses are configured per test; it honors If-None-Match against the current ETag.
    /// </summary>
    private sealed class Origin
    {
        public string Body = "v1";
        public string ETag = "\"v1\"";
        public string? CacheControl = "max-age=60";
        public string? Vary;
        public DateTimeOffset? Expires;
        public int BodyResponses;
        public int NotModifiedResponses;
        public FakeHandler Handler { get; }

        public Origin(ManualClock clock)
        {
            Handler = new FakeHandler(request =>
            {
                if (request.Method != HttpMethod.Get)
                {
                    return FakeHandler.Respond(HttpStatusCode.NoContent);
                }
                HttpResponseMessage response;
                if (request.Headers.IfNoneMatch.Any(t => t.Tag == ETag))
                {
                    NotModifiedResponses++;
                    response = new HttpResponseMessage(HttpStatusCode.NotModified) { Content = new ByteArrayContent([]) };
                }
                else
                {
                    BodyResponses++;
                    var language = request.Headers.AcceptLanguage.FirstOrDefault()?.Value;
                    response = FakeHandler.Respond(HttpStatusCode.OK, language == null ? Body : $"{Body}-{language}", "text/plain");
                }
                response.Headers.ETag = new EntityTagHeaderValue(ETag);
                response.Headers.Date = clock.GetUtcNow();
                if (CacheControl != null)
                {
                    response.Headers.TryAddWithoutValidation("Cache-Control", CacheControl);
                }
                if (Vary != null)
                {
                    response.Headers.TryAddWithoutValidation("Vary", Vary);
                }
                if (Expires != null)
                {
                    response.Content.Headers.Expires = Expires;
                }
                return response;
            });
        }

        public int Requests => Handler.Requests.Count;
    }

    private static (NexarClient Client, Origin Origin, ManualClock Clock, MemoryHttpCache Cache) Setup(Action<Origin>? configure = null)
    {
        var clock = new ManualClock();
        var origin = new Origin(clock);
        configure?.Invoke(origin);
        var cache = new MemoryHttpCache();
        var client = TestClient.Create(origin.Handler, b => b.Cache(cache, clock));
        return (client, origin, clock, cache);
    }

    private static async Task<(string Body, CacheStatus Status, NexarResponse Response)> Get(NexarClient client, string path = "/doc", Action<RequestBuilder>? configure = null)
    {
        var builder = client.Get(path);
        configure?.Invoke(builder);
        var response = await builder.Send();
        return (await response.Text(), response.CacheStatus, response);
    }

    [Fact]
    public async Task FreshResponseIsServedFromCache()
    {
        var (client, origin, clock, _) = Setup();
        using var _ = client;

        var first = await Get(client);
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = await Get(client);

        Assert.Equal(CacheStatus.Miss, first.Status);
        Assert.Equal(CacheStatus.Hit, second.Status);
        Assert.Equal("v1", second.Body);
        Assert.Equal(1, origin.Requests);
        Assert.Equal(TimeSpan.FromSeconds(30), second.Response.Headers.Age);
    }

    [Fact]
    public async Task StaleResponseIsRevalidatedWith304()
    {
        var (client, origin, clock, _) = Setup();
        using var _ = client;

        await Get(client);
        clock.Advance(TimeSpan.FromSeconds(61));
        var revalidated = await Get(client);
        var afterRevalidation = await Get(client);

        Assert.Equal(CacheStatus.Revalidated, revalidated.Status);
        Assert.Equal("v1", revalidated.Body);
        Assert.Equal(HttpStatusCode.OK, revalidated.Response.StatusCode);
        Assert.Equal("\"v1\"", origin.Handler.Requests[1].Headers["If-None-Match"]);
        Assert.Equal(1, origin.NotModifiedResponses);
        Assert.Equal(CacheStatus.Hit, afterRevalidation.Status);   // the 304 refreshed the freshness
    }

    [Fact]
    public async Task ChangedResourceReplacesTheEntry()
    {
        var (client, origin, clock, _) = Setup();
        using var _ = client;

        await Get(client);
        clock.Advance(TimeSpan.FromSeconds(61));
        origin.Body = "v2";
        origin.ETag = "\"v2\"";
        var changed = await Get(client);
        var cached = await Get(client);

        Assert.Equal(CacheStatus.Miss, changed.Status);
        Assert.Equal("v2", changed.Body);
        Assert.Equal(CacheStatus.Hit, cached.Status);
        Assert.Equal("v2", cached.Body);
    }

    [Fact]
    public async Task NoStoreIsNeverCached()
    {
        var (client, origin, _, cache) = Setup(o => o.CacheControl = "no-store");
        using var _ = client;

        await Get(client);
        var second = await Get(client);

        Assert.Equal(CacheStatus.Miss, second.Status);
        Assert.Equal(2, origin.BodyResponses);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task NoCacheResponseIsRevalidatedEveryTime()
    {
        var (client, origin, _, _) = Setup(o => o.CacheControl = "no-cache");
        using var _ = client;

        await Get(client);
        var second = await Get(client);
        var third = await Get(client);

        Assert.Equal(CacheStatus.Revalidated, second.Status);
        Assert.Equal(CacheStatus.Revalidated, third.Status);
        Assert.Equal(1, origin.BodyResponses);
        Assert.Equal(2, origin.NotModifiedResponses);
    }

    [Fact]
    public async Task ExpiresGivesFreshness()
    {
        var clockStart = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var (client, origin, clock, _) = Setup(o =>
        {
            o.CacheControl = null;
            o.Expires = clockStart.AddMinutes(5);
        });
        using var _ = client;

        await Get(client);
        clock.Advance(TimeSpan.FromMinutes(4));
        var fresh = await Get(client);
        clock.Advance(TimeSpan.FromMinutes(2));
        var stale = await Get(client);

        Assert.Equal(CacheStatus.Hit, fresh.Status);
        Assert.Equal(CacheStatus.Revalidated, stale.Status);
        Assert.Equal(1, origin.BodyResponses);
    }

    [Fact]
    public async Task VaryKeepsSeparateEntries()
    {
        var (client, origin, _, _) = Setup(o => o.Vary = "Accept-Language");
        using var _ = client;

        var en1 = await Get(client, configure: b => b.AcceptLanguage("en"));
        var tr1 = await Get(client, configure: b => b.AcceptLanguage("tr"));
        var en2 = await Get(client, configure: b => b.AcceptLanguage("en"));
        var tr2 = await Get(client, configure: b => b.AcceptLanguage("tr"));

        Assert.Equal((CacheStatus.Miss, CacheStatus.Miss), (en1.Status, tr1.Status));
        Assert.Equal((CacheStatus.Hit, CacheStatus.Hit), (en2.Status, tr2.Status));
        Assert.Equal(("v1-en", "v1-tr"), (en2.Body, tr2.Body));
        Assert.Equal(2, origin.BodyResponses);
    }

    [Fact]
    public async Task VaryStarIsNotCached()
    {
        var (client, origin, _, _) = Setup(o => o.Vary = "*");
        using var _ = client;

        await Get(client);
        var second = await Get(client);

        Assert.Equal(CacheStatus.Miss, second.Status);
        Assert.Equal(2, origin.BodyResponses);
    }

    [Fact]
    public async Task UnsafeRequestInvalidatesTheUrl()
    {
        var (client, origin, _, _) = Setup();
        using var _ = client;

        await Get(client);
        using (await client.Put("/doc").Body("new").Send()) { }
        var afterPut = await Get(client);

        Assert.Equal(CacheStatus.Miss, afterPut.Status);
        Assert.Equal(2, origin.BodyResponses);
    }

    [Fact]
    public async Task NoCacheRequestRevalidates()
    {
        var (client, origin, _, _) = Setup();
        using var _ = client;

        await Get(client);
        var forced = await Get(client, configure: b => b.NoCache());

        Assert.Equal(CacheStatus.Revalidated, forced.Status);
        Assert.Equal(1, origin.NotModifiedResponses);
    }

    [Fact]
    public async Task RequestCacheControlNoStoreBypasses()
    {
        var (client, origin, _, _) = Setup();
        using var _ = client;

        await Get(client);
        var bypassed = await Get(client, configure: b => b.Header("Cache-Control", "no-store"));

        Assert.Equal(CacheStatus.None, bypassed.Status);
        Assert.Equal(2, origin.BodyResponses);
    }

    [Fact]
    public async Task OnlyIfCached()
    {
        var (client, origin, clock, _) = Setup();
        using var _ = client;

        var empty = await Get(client, configure: b => b.OnlyIfCached());
        await Get(client);
        var cached = await Get(client, configure: b => b.OnlyIfCached());
        clock.Advance(TimeSpan.FromMinutes(5));
        var stale = await Get(client, configure: b => b.OnlyIfCached());

        Assert.Equal(HttpStatusCode.GatewayTimeout, empty.Response.StatusCode);
        Assert.Equal(CacheStatus.Hit, cached.Status);
        Assert.Equal(HttpStatusCode.GatewayTimeout, stale.Response.StatusCode);
        Assert.Equal(1, origin.Requests);
    }

    [Fact]
    public async Task StaleWhileRevalidateServesStaleAndRefreshesInTheBackground()
    {
        var (client, origin, clock, _) = Setup(o => o.CacheControl = "max-age=60, stale-while-revalidate=30");
        using var _ = client;

        await Get(client);
        clock.Advance(TimeSpan.FromSeconds(70));
        origin.Body = "v2";
        origin.ETag = "\"v2\"";
        var stale = await Get(client);

        Assert.Equal(CacheStatus.Stale, stale.Status);
        Assert.Equal("v1", stale.Body);

        // Wait for the background refresh to store v2.
        for (var i = 0; i < 100 && origin.BodyResponses < 2; i++)
        {
            await Task.Delay(10);
        }
        await Task.Delay(50);
        var refreshed = await Get(client);

        Assert.Equal(CacheStatus.Hit, refreshed.Status);
        Assert.Equal("v2", refreshed.Body);
    }

    [Fact]
    public async Task MustRevalidateDisablesStaleWhileRevalidate()
    {
        var (client, _, clock, _) = Setup(o => o.CacheControl = "max-age=60, stale-while-revalidate=30, must-revalidate");
        using var _ = client;

        await Get(client);
        clock.Advance(TimeSpan.FromSeconds(70));
        var result = await Get(client);

        Assert.Equal(CacheStatus.Revalidated, result.Status);
    }

    [Fact]
    public async Task ConditionalAndRangedRequestsBypassTheCache()
    {
        var (client, _, _, _) = Setup();
        using var _ = client;

        await Get(client);
        var conditional = await Get(client, configure: b => b.IfNoneMatch("other"));
        var ranged = await Get(client, configure: b => b.Range(0, 1));

        Assert.Equal(CacheStatus.None, conditional.Status);
        Assert.Equal(CacheStatus.None, ranged.Status);
    }

    private sealed class StreamedContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static FakeHandler Cacheable(Func<HttpContent> content, Action? onRequest = null) => new(_ =>
    {
        onRequest?.Invoke();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content() };
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
        return response;
    });

    [Fact]
    public async Task BodiesWithoutLengthAreCachedWhenSmall()
    {
        var requests = 0;
        var handler = Cacheable(() => new StreamedContent(Encoding.UTF8.GetBytes("small")), () => requests++);
        using var client = TestClient.Create(handler, b => b.Cache(new MemoryHttpCache(maxEntryBytes: 1024)));

        var first = await client.Get("/").Send().Text();
        using var second = await client.Get("/").Send();

        Assert.Equal("small", first);
        Assert.Equal(CacheStatus.Hit, second.CacheStatus);
        Assert.Equal("small", await second.Text());
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LargeBodiesAreNotCachedButStillComplete(bool declareLength)
    {
        var data = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        var handler = Cacheable(() => declareLength ? new ByteArrayContent(data) : new StreamedContent(data));
        var cache = new MemoryHttpCache(maxEntryBytes: 100_000);
        using var client = TestClient.Create(handler, b => b.Cache(cache));

        var bytes = await client.Get("/big").Send().Bytes();

        Assert.Equal(data, bytes);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task WithoutACacheStatusIsNone()
    {
        using var client = TestClient.Create(new FakeHandler());

        using var res = await client.Get("/").Send();

        Assert.Equal(CacheStatus.None, res.CacheStatus);
    }

    [Fact]
    public async Task MemoryCacheEvictsLeastRecentlyUsed()
    {
        var cache = new MemoryHttpCache(maxSizeBytes: 3_000, maxEntryBytes: 1_000);
        CachedResponse Entry(int size) => new()
        {
            Url = new Uri("https://x.test"),
            StatusCode = 200,
            Headers = [],
            ContentHeaders = [],
            Body = new byte[size],
            RequestTime = DateTimeOffset.UtcNow,
            ResponseTime = DateTimeOffset.UtcNow
        };

        await cache.SetAsync("a", Entry(700), default);
        await cache.SetAsync("b", Entry(700), default);
        await cache.SetAsync("c", Entry(700), default);
        await cache.GetAsync("a", default);            // a is now the most recent
        await cache.SetAsync("d", Entry(700), default);  // evicts b

        Assert.NotNull(await cache.GetAsync("a", default));
        Assert.Null(await cache.GetAsync("b", default));
        Assert.NotNull(await cache.GetAsync("d", default));
        Assert.True(cache.SizeBytes <= cache.MaxSizeBytes);
    }
}
