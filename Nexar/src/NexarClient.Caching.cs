using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Nexar;

public sealed partial class NexarClient
{
    /// <summary>
    /// Serves GET requests from the cache when allowed (RFC 9111), revalidates stale entries, and stores new responses.
    /// </summary>
    private async Task<NexarResponse> SendThroughCacheAsync(PreparedRequest request, AttemptState attempts, CancellationToken cancellationToken)
    {
        if (_options.Cache is not { } cache)
        {
            return await SendAsync(request, attempts, cancellationToken).ConfigureAwait(false);
        }

        if (request.Method != HttpMethod.Get)
        {
            var result = await SendAsync(request, attempts, cancellationToken).ConfigureAwait(false);
            // RFC 9111 §4.4: a successful unsafe request invalidates the stored response for its URL.
            if (request.Method != HttpMethod.Head && request.Method != HttpMethod.Options && request.Method != HttpMethod.Trace && result.Status < 400)
            {
                await cache.RemoveAsync(CacheRules.PrimaryKey(request.Url), cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        if (CacheRules.BypassesCache(request))
        {
            return await SendAsync(request, attempts, cancellationToken).ConfigureAwait(false);
        }

        var clock = _options.CacheClock;
        var (entry, key) = await LookupAsync(cache, request, cancellationToken).ConfigureAwait(false);
        var revalidate = request.CacheMode == CacheMode.Revalidate || CacheRules.RequestDirectives(request).ContainsKey("no-cache");
        var fresh = false;

        if (entry != null)
        {
            var now = clock.GetUtcNow();
            var age = CacheRules.CurrentAge(entry, now);
            var lifetime = CacheRules.FreshnessLifetime(entry) ?? TimeSpan.Zero;
            var directives = CacheRules.ResponseDirectives(entry);
            var mustRevalidateAlways = directives.ContainsKey("no-cache");
            fresh = lifetime > age && !mustRevalidateAlways;

            if (fresh && !revalidate)
            {
                return FromCache(entry, age, CacheStatus.Hit, request);
            }

            if (!fresh && !revalidate && !mustRevalidateAlways && !directives.ContainsKey("must-revalidate")
                && CacheRules.Seconds(directives, "stale-while-revalidate") is { } window && age < lifetime + window)
            {
                _ = Task.Run(() => RefreshInBackgroundAsync(cache, request, entry, key));
                return FromCache(entry, age, CacheStatus.Stale, request);
            }
        }

        if (request.CacheMode == CacheMode.OnlyIfCached)
        {
            var timeout = new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            {
                Content = new ByteArrayContent([]),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.Url)
            };
            return new NexarResponse(timeout, request.Url, JsonOptions, new CancellationTokenSource()) { CacheStatus = CacheStatus.Miss };
        }

        var conditional = entry != null && CacheRules.HasValidators(entry);
        var outgoing = conditional ? CacheRules.WithValidators(request, entry!) : request;
        var requestTime = clock.GetUtcNow();
        var response = await SendAsync(outgoing, attempts, cancellationToken).ConfigureAwait(false);
        var responseTime = clock.GetUtcNow();

        if (conditional && response.StatusCode == HttpStatusCode.NotModified)
        {
            var refreshed = CacheRules.Refresh(entry!, response.HttpResponseMessage, requestTime, responseTime);
            response.Dispose();
            await StoreAsync(cache, request, refreshed, cancellationToken).ConfigureAwait(false);
            return FromCache(refreshed, CacheRules.CurrentAge(refreshed, responseTime), CacheStatus.Revalidated, request);
        }

        await TryStoreAsync(cache, request, response, requestTime, responseTime, cancellationToken).ConfigureAwait(false);
        response.CacheStatus = CacheStatus.Miss;
        return response;
    }

    private static async Task<(CachedResponse? Entry, string Key)> LookupAsync(IHttpCache cache, PreparedRequest request, CancellationToken cancellationToken)
    {
        var primaryKey = CacheRules.PrimaryKey(request.Url);
        var primary = await cache.GetAsync(primaryKey, cancellationToken).ConfigureAwait(false);
        if (primary == null || primary.VaryHeaders.Count == 0)
        {
            return (primary, primaryKey);
        }

        // The primary entry only records which request headers the URL varies on.
        var variantKey = CacheRules.VariantKey(request, primary.VaryHeaders);
        return (await cache.GetAsync(variantKey, cancellationToken).ConfigureAwait(false), variantKey);
    }

    private static async Task StoreAsync(IHttpCache cache, PreparedRequest request, CachedResponse entry, CancellationToken cancellationToken)
    {
        var primaryKey = CacheRules.PrimaryKey(request.Url);
        if (entry.VaryHeaders.Count == 0)
        {
            await cache.SetAsync(primaryKey, entry, cancellationToken).ConfigureAwait(false);
            return;
        }
        await cache.SetAsync(primaryKey, CacheRules.VaryMarker(entry), cancellationToken).ConfigureAwait(false);
        await cache.SetAsync(CacheRules.VariantKey(request, entry.VaryHeaders), entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Buffers and stores a cacheable response of at most <see cref="IHttpCache.MaxEntryBytes"/>. A larger body is
    /// handed back unbuffered: what was read so far is replayed in front of the rest.
    /// </summary>
    private async Task TryStoreAsync(IHttpCache cache, PreparedRequest request, NexarResponse response,
        DateTimeOffset requestTime, DateTimeOffset responseTime, CancellationToken cancellationToken)
    {
        var message = response.HttpResponseMessage;
        if (!CacheRules.MayStore(request, message) || message.Content.Headers.ContentLength > cache.MaxEntryBytes)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = request.Timeout ?? _options.Timeout;
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(timeout);
        }

        var original = message.Content;
        var stream = await original.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var complete = false;
        try
        {
            while (buffer.Length <= cache.MaxEntryBytes)
            {
                var read = await stream.ReadAsync(chunk, timeoutCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    complete = true;
                    break;
                }
                buffer.Write(chunk, 0, read);
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Not cacheable after all; give the caller what arrived and let its read report the failure.
            ReplaceContent(message, original, new PrefixedStream(buffer.ToArray(), stream));
            return;
        }

        var bytes = buffer.ToArray();
        if (!complete)
        {
            ReplaceContent(message, original, new PrefixedStream(bytes, stream));
            return;
        }

        await stream.DisposeAsync().ConfigureAwait(false);
        ReplaceContent(message, original, new MemoryStream(bytes));
        var entry = CacheRules.CreateEntry(message, request.Url, bytes, requestTime, responseTime);
        await StoreAsync(cache, request, entry, cancellationToken).ConfigureAwait(false);
    }

    private static void ReplaceContent(HttpResponseMessage message, HttpContent original, Stream body)
    {
        var content = new StreamContent(body);
        foreach (var header in original.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        message.Content = content;
    }

    private NexarResponse FromCache(CachedResponse entry, TimeSpan age, CacheStatus status, PreparedRequest request) =>
        new(CacheRules.ToResponseMessage(entry, age), request.Url, JsonOptions, new CancellationTokenSource(), request.MaxResponseSize)
        {
            CacheStatus = status
        };

    /// <summary>
    /// Revalidates a stale entry served under stale-while-revalidate. Failures are ignored; the next request retries.
    /// </summary>
    private async Task RefreshInBackgroundAsync(IHttpCache cache, PreparedRequest request, CachedResponse entry, string key)
    {
        try
        {
            var attempts = new AttemptState(null, default(TagList), request.Url.AbsoluteUri);
            var outgoing = CacheRules.HasValidators(entry) ? CacheRules.WithValidators(request, entry) : request;
            var requestTime = _options.CacheClock.GetUtcNow();
            using var response = await SendAsync(outgoing, attempts, CancellationToken.None).ConfigureAwait(false);
            var responseTime = _options.CacheClock.GetUtcNow();
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                await StoreAsync(cache, request, CacheRules.Refresh(entry, response.HttpResponseMessage, requestTime, responseTime), CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await TryStoreAsync(cache, request, response, requestTime, responseTime, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is NexarException or HttpRequestException or IOException or OperationCanceledException)
        {
            _options.Logger.LogDebug(ex, "Background revalidation of {Key} failed", key);
        }
    }
}
