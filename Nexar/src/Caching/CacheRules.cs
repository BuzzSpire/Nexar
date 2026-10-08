using System.Globalization;
using System.Net;

namespace Nexar;

/// <summary>
/// The RFC 9111 rules a private client cache follows: what may be stored, freshness, age and keys.
/// </summary>
internal static class CacheRules
{
    private static readonly HashSet<int> CacheableStatuses = [200, 203, 204, 300, 301, 308, 404, 405, 410, 414, 501];

    // Hop-by-hop headers describe one connection, not the stored response.
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Connection", "Transfer-Encoding", "Upgrade", "TE", "Trailer"
    };

    private static readonly string[] ConditionalHeaders = ["If-None-Match", "If-Modified-Since", "If-Match", "If-Unmodified-Since", "If-Range", "Range"];

    /// <summary>Cache-Control directives, lower-cased, with their values (null when there is none).</summary>
    public static Dictionary<string, string?> ParseCacheControl(IEnumerable<string>? values)
    {
        var directives = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (values == null)
        {
            return directives;
        }
        foreach (var part in values.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var equals = part.IndexOf('=');
            if (equals < 0)
            {
                directives.TryAdd(part, null);
            }
            else
            {
                directives.TryAdd(part[..equals].Trim(), part[(equals + 1)..].Trim().Trim('"'));
            }
        }
        return directives;
    }

    public static TimeSpan? Seconds(Dictionary<string, string?> directives, string name) =>
        directives.TryGetValue(name, out var value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>True if the request must skip the cache entirely: not a GET, conditional or ranged, or no-store.</summary>
    public static bool BypassesCache(PreparedRequest request)
    {
        if (request.Method != HttpMethod.Get || ConditionalHeaders.Any(request.Headers.ContainsKey))
        {
            return true;
        }
        return RequestDirectives(request).ContainsKey("no-store");
    }

    public static Dictionary<string, string?> RequestDirectives(PreparedRequest request) =>
        ParseCacheControl(request.Headers.TryGetValue("Cache-Control", out var values) ? values : null);

    public static string PrimaryKey(Uri url) => "GET " + url.GetLeftPart(UriPartial.Query);

    public static string VariantKey(PreparedRequest request, IReadOnlyList<string> varyHeaders) =>
        PrimaryKey(request.Url) + "\n" + string.Join("\n", varyHeaders.Select(name =>
            $"{name}:{(request.Headers.TryGetValue(name, out var values) ? string.Join(",", values) : "")}"));

    public static Dictionary<string, string?> ResponseDirectives(CachedResponse entry) =>
        ParseCacheControl(Values(entry.Headers, "Cache-Control"));

    /// <summary>How long the response is fresh: <c>max-age</c>, else <c>Expires</c> minus <c>Date</c>; null if neither is given.</summary>
    public static TimeSpan? FreshnessLifetime(CachedResponse entry)
    {
        if (Seconds(ResponseDirectives(entry), "max-age") is { } maxAge)
        {
            return maxAge;
        }
        if (Values(entry.ContentHeaders, "Expires")?.FirstOrDefault() is { } expires)
        {
            // An invalid Expires (e.g. "0") means already expired.
            return ParseDate(expires) is { } expiresAt ? expiresAt - (Date(entry) ?? entry.ResponseTime) : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>RFC 9111 §4.2.3 current age.</summary>
    public static TimeSpan CurrentAge(CachedResponse entry, DateTimeOffset now)
    {
        var date = Date(entry) ?? entry.ResponseTime;
        var apparentAge = Max(TimeSpan.Zero, entry.ResponseTime - date);
        var ageValue = Values(entry.Headers, "Age")?.FirstOrDefault() is { } age && long.TryParse(age, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
        var correctedAge = ageValue + (entry.ResponseTime - entry.RequestTime);
        var initialAge = Max(apparentAge, correctedAge);
        return initialAge + Max(TimeSpan.Zero, now - entry.ResponseTime);
    }

    public static bool HasValidators(CachedResponse entry) =>
        Values(entry.Headers, "ETag") != null || Values(entry.ContentHeaders, "Last-Modified") != null;

    /// <summary>The request with <c>If-None-Match</c> / <c>If-Modified-Since</c> from the stored validators.</summary>
    public static PreparedRequest WithValidators(PreparedRequest request, CachedResponse entry)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(request.Headers, StringComparer.OrdinalIgnoreCase);
        if (Values(entry.Headers, "ETag") is { } etag)
        {
            headers["If-None-Match"] = etag;
        }
        if (Values(entry.ContentHeaders, "Last-Modified") is { } lastModified)
        {
            headers["If-Modified-Since"] = lastModified;
        }
        return request with { Headers = headers };
    }

    /// <summary>
    /// The parts of a live response that decide whether it may be stored (before its body is read).
    /// </summary>
    public static bool MayStore(PreparedRequest request, HttpResponseMessage response)
    {
        if (!CacheableStatuses.Contains((int)response.StatusCode))
        {
            return false;
        }
        var directives = ParseCacheControl(response.Headers.TryGetValues("Cache-Control", out var values) ? values : null);
        if (directives.ContainsKey("no-store") || RequestDirectives(request).ContainsKey("no-store"))
        {
            return false;
        }
        if (response.Headers.Vary.Contains("*"))
        {
            return false;
        }
        var explicitFreshness = directives.ContainsKey("max-age") || response.Content.Headers.Expires != null || response.Content.Headers.Contains("Expires");
        var validators = response.Headers.ETag != null || response.Content.Headers.LastModified != null;
        return explicitFreshness || validators;
    }

    public static CachedResponse CreateEntry(HttpResponseMessage response, Uri url, byte[] body, DateTimeOffset requestTime, DateTimeOffset responseTime) => new()
    {
        Url = url,
        StatusCode = (int)response.StatusCode,
        ReasonPhrase = response.ReasonPhrase,
        Headers = response.Headers.Where(h => !HopByHop.Contains(h.Key)).Select(h => KeyValuePair.Create(h.Key, h.Value.ToArray())).ToList(),
        ContentHeaders = response.Content.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToArray())).ToList(),
        Body = body,
        RequestTime = requestTime,
        ResponseTime = responseTime,
        VaryHeaders = response.Headers.Vary.Select(v => v.ToLowerInvariant()).ToList()
    };

    /// <summary>RFC 9111 §4.3.4: a 304 refreshes the stored headers; the stored body stays.</summary>
    public static CachedResponse Refresh(CachedResponse entry, HttpResponseMessage notModified, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        var updated = notModified.Headers.Where(h => !HopByHop.Contains(h.Key)).ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        var headers = entry.Headers.Where(h => !updated.ContainsKey(h.Key)).Concat(updated.Select(h => KeyValuePair.Create(h.Key, h.Value))).ToList();
        var contentUpdates = notModified.Content.Headers
            .Where(h => h.Key is "Expires" or "Last-Modified")
            .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        var contentHeaders = entry.ContentHeaders.Where(h => !contentUpdates.ContainsKey(h.Key)).Concat(contentUpdates.Select(h => KeyValuePair.Create(h.Key, h.Value))).ToList();

        return new CachedResponse
        {
            Url = entry.Url,
            StatusCode = entry.StatusCode,
            ReasonPhrase = entry.ReasonPhrase,
            Headers = headers,
            ContentHeaders = contentHeaders,
            Body = entry.Body,
            RequestTime = requestTime,
            ResponseTime = responseTime,
            VaryHeaders = entry.VaryHeaders
        };
    }

    /// <summary>A body-less copy, stored under the primary key to remember which headers a URL varies on.</summary>
    public static CachedResponse VaryMarker(CachedResponse entry) => new()
    {
        Url = entry.Url,
        StatusCode = entry.StatusCode,
        Headers = [],
        ContentHeaders = [],
        Body = [],
        RequestTime = entry.RequestTime,
        ResponseTime = entry.ResponseTime,
        VaryHeaders = entry.VaryHeaders
    };

    public static HttpResponseMessage ToResponseMessage(CachedResponse entry, TimeSpan age)
    {
        var message = new HttpResponseMessage((HttpStatusCode)entry.StatusCode)
        {
            ReasonPhrase = entry.ReasonPhrase,
            Content = new ByteArrayContent(entry.Body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, entry.Url)
        };
        foreach (var (name, values) in entry.Headers.Where(h => !h.Key.Equals("Age", StringComparison.OrdinalIgnoreCase)))
        {
            message.Headers.TryAddWithoutValidation(name, values);
        }
        message.Headers.TryAddWithoutValidation("Age", ((long)Max(TimeSpan.Zero, age).TotalSeconds).ToString(CultureInfo.InvariantCulture));
        foreach (var (name, values) in entry.ContentHeaders)
        {
            message.Content.Headers.Remove(name);
            message.Content.Headers.TryAddWithoutValidation(name, values);
        }
        return message;
    }

    private static string[]? Values(IReadOnlyList<KeyValuePair<string, string[]>> headers, string name) =>
        headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static DateTimeOffset? Date(CachedResponse entry) =>
        Values(entry.Headers, "Date")?.FirstOrDefault() is { } date ? ParseDate(date) : null;

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
            ? date
            : null;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>
/// Reads an already-consumed prefix, then the rest of the original stream.
/// </summary>
internal sealed class PrefixedStream(byte[] prefix, Stream rest) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        return rest.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        return await rest.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rest.Dispose();
        }
        base.Dispose(disposing);
    }
}
