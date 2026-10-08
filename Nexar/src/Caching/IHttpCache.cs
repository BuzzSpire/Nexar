namespace Nexar;

/// <summary>
/// Storage for <see cref="ClientBuilder.Cache"/>. Nexar decides what to store and when entries are fresh
/// (RFC 9111); an implementation only keeps entries by key. <see cref="MemoryHttpCache"/> is the built-in one;
/// implement this for a disk or distributed cache.
/// </summary>
/// <remarks>Implementations must be thread-safe.</remarks>
public interface IHttpCache
{
    /// <summary>Responses with a larger body are not cached; their bodies are never buffered for the cache.</summary>
    long MaxEntryBytes { get; }

    /// <summary>Returns the entry for <paramref name="key"/>, or null.</summary>
    ValueTask<CachedResponse?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores or replaces the entry for <paramref name="key"/>.</summary>
    ValueTask SetAsync(string key, CachedResponse entry, CancellationToken cancellationToken);

    /// <summary>Removes the entry for <paramref name="key"/>, if any.</summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// A stored response. Header lists keep the original names and values.
/// </summary>
public sealed class CachedResponse
{
    /// <summary>The URL the response was for.</summary>
    public required Uri Url { get; init; }

    /// <summary>The status code.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The reason phrase, if any.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>The response headers.</summary>
    public required IReadOnlyList<KeyValuePair<string, string[]>> Headers { get; init; }

    /// <summary>The content headers (<c>Content-Type</c>, ...).</summary>
    public required IReadOnlyList<KeyValuePair<string, string[]>> ContentHeaders { get; init; }

    /// <summary>The body.</summary>
    public required byte[] Body { get; init; }

    /// <summary>When the request that produced this response was sent.</summary>
    public required DateTimeOffset RequestTime { get; init; }

    /// <summary>When the response was received.</summary>
    public required DateTimeOffset ResponseTime { get; init; }

    /// <summary>The request header names listed in <c>Vary</c>, lower-cased.</summary>
    public IReadOnlyList<string> VaryHeaders { get; init; } = [];

    /// <summary>An estimate of the memory this entry uses, for size-bounded caches.</summary>
    public long Size => Body.Length
        + Headers.Sum(h => h.Key.Length + h.Value.Sum(v => v.Length))
        + ContentHeaders.Sum(h => h.Key.Length + h.Value.Sum(v => v.Length))
        + 256;
}

/// <summary>
/// How a response relates to the client's cache. See <see cref="NexarResponse.CacheStatus"/>.
/// </summary>
public enum CacheStatus
{
    /// <summary>No cache is configured, or the request bypasses it (not a GET, conditional, ranged, <c>no-store</c>).</summary>
    None,

    /// <summary>Not in the cache (or not usable); the response came from the server.</summary>
    Miss,

    /// <summary>Served from the cache without contacting the server.</summary>
    Hit,

    /// <summary>The server confirmed the cached copy with <c>304 Not Modified</c>; the cached body was served.</summary>
    Revalidated,

    /// <summary>A stale copy was served under <c>stale-while-revalidate</c> while it is refreshed in the background.</summary>
    Stale
}
