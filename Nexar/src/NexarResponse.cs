using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nexar;

/// <summary>
/// An HTTP response. The body is not read until you call
/// <see cref="Text"/>, <see cref="Json{T}"/>, <see cref="Bytes"/> or <see cref="Stream"/>.
/// </summary>
public sealed class NexarResponse : IDisposable
{
    /// <summary>How much of an error body <see cref="ErrorForStatus"/> keeps.</summary>
    internal const int MaxErrorBodyBytes = 64 * 1024;

    private readonly HttpResponseMessage _response;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CancellationTokenSource _deadline;
    private bool _buffered;

    internal NexarResponse(HttpResponseMessage response, Uri url, JsonSerializerOptions jsonOptions, CancellationTokenSource deadline)
    {
        _response = response;
        _jsonOptions = jsonOptions;
        _deadline = deadline;
        Url = response.RequestMessage?.RequestUri ?? url;
    }

    /// <summary>The status code.</summary>
    public HttpStatusCode StatusCode => _response.StatusCode;

    /// <summary>The status code as an integer.</summary>
    public int Status => (int)_response.StatusCode;

    /// <summary>True for a 2xx status.</summary>
    public bool IsSuccess => _response.IsSuccessStatusCode;

    /// <summary>True for <c>304 Not Modified</c>, the answer to a conditional request whose cached copy is still valid.</summary>
    public bool IsNotModified => _response.StatusCode == HttpStatusCode.NotModified;

    /// <summary>The reason phrase sent by the server, if any.</summary>
    public string? ReasonPhrase => _response.ReasonPhrase;

    /// <summary>The response headers.</summary>
    public HttpResponseHeaders Headers => _response.Headers;

    /// <summary>The body headers (<c>Content-Type</c>, <c>Content-Length</c>, ...).</summary>
    public HttpContentHeaders ContentHeaders => _response.Content.Headers;

    /// <summary>The body length, if the server sent it.</summary>
    public long? ContentLength => _response.Content.Headers.ContentLength;

    /// <summary>The final URL of the response, after redirects.</summary>
    public Uri Url { get; }

    /// <summary>The HTTP version of the response.</summary>
    public Version Version => _response.Version;

    /// <summary>The underlying response, for anything Nexar does not expose.</summary>
    public HttpResponseMessage HttpResponseMessage => _response;

    /// <summary>The parsed <c>Content-Type</c>, or null.</summary>
    public MediaTypeHeaderValue? ContentType => _response.Content.Headers.ContentType;

    /// <summary>The parsed <c>ETag</c>, or null.</summary>
    public EntityTagHeaderValue? ETag => _response.Headers.ETag;

    /// <summary>The parsed <c>Last-Modified</c>, or null.</summary>
    public DateTimeOffset? LastModified => _response.Content.Headers.LastModified;

    /// <summary>The <c>Location</c> header resolved against <see cref="Url"/>, or null.</summary>
    public Uri? Location => _response.Headers.Location is { } location
        ? (location.IsAbsoluteUri ? location : new Uri(Url, location))
        : null;

    /// <summary>
    /// How long the server asks to wait, from <c>Retry-After</c> given in seconds or as a date, or null.
    /// </summary>
    public TimeSpan? RetryAfter => _response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
        _ => null
    };

    /// <summary>
    /// The value of any response or content header, with multiple values joined by <c>", "</c>; null if missing.
    /// </summary>
    public string? Header(string name)
    {
        if (_response.Headers.TryGetValues(name, out var values) || _response.Content.Headers.TryGetValues(name, out values))
        {
            return string.Join(", ", values);
        }
        return null;
    }

    /// <summary>
    /// Throws if the status is 4xx or 5xx; otherwise returns this response.
    /// The exception keeps the start of the error body (<see cref="NexarException.ResponseBody"/>)
    /// and the response headers.
    /// </summary>
    /// <exception cref="NexarException">The status is 4xx or 5xx.</exception>
    public async Task<NexarResponse> ErrorForStatus(CancellationToken cancellationToken = default)
    {
        if (Status < 400)
        {
            return this;
        }

        var (body, truncated) = await ReadErrorBodyAsync(cancellationToken).ConfigureAwait(false);
        var headers = _response.Headers
            .Concat(_response.Content.Headers)
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.SelectMany(h => h.Value).ToList(), StringComparer.OrdinalIgnoreCase);
        var problem = body != null && !truncated && NexarProblemDetails.IsProblemMediaType(ContentType?.MediaType)
            ? NexarProblemDetails.TryParse(body)
            : null;
        var message = $"HTTP {Status} ({ReasonPhrase ?? StatusCode.ToString()}) for {Url}";
        if (problem?.Title != null)
        {
            message += $": {problem.Title}";
        }
        Dispose();

        throw new NexarException(ErrorKind.Status, message, Url, StatusCode)
        {
            ResponseBody = body,
            IsResponseBodyTruncated = truncated,
            ResponseHeaders = headers,
            Problem = problem,
            JsonOptions = _jsonOptions
        };
    }

    /// <summary>
    /// Reads the body as text, using the charset from <c>Content-Type</c>, else a byte order mark, else UTF-8.
    /// </summary>
    /// <exception cref="NexarException">The charset is unknown (<see cref="ErrorKind.Decode"/>).</exception>
    public Task<string> Text(CancellationToken cancellationToken = default) => Text(null, cancellationToken);

    /// <summary>
    /// Reads the body as text, using the charset from <c>Content-Type</c>, else a byte order mark,
    /// else <paramref name="fallback"/>. Legacy code pages (windows-1254, Shift_JIS, ...) need
    /// <c>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)</c> at startup.
    /// </summary>
    /// <exception cref="NexarException">The charset is unknown (<see cref="ErrorKind.Decode"/>).</exception>
    public async Task<string> Text(Encoding? fallback, CancellationToken cancellationToken = default)
    {
        var bytes = await Bytes(cancellationToken).ConfigureAwait(false);

        Encoding encoding;
        var charset = ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException ex)
            {
                throw new NexarException(ErrorKind.Decode, $"Cannot decode text: unknown charset '{charset}'.", Url, innerException: ex);
            }
        }
        else
        {
            encoding = DetectByteOrderMark(bytes) ?? fallback ?? Encoding.UTF8;
        }

        var preamble = encoding.Preamble;
        var skip = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        return encoding.GetString(bytes, skip, bytes.Length - skip);
    }

    private static Encoding? DetectByteOrderMark(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8,
        [0xFF, 0xFE, 0x00, 0x00, ..] => Encoding.UTF32,
        [0xFF, 0xFE, ..] => Encoding.Unicode,
        [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
        _ => null
    };

    /// <summary>
    /// Reads the body as bytes.
    /// </summary>
    public async Task<byte[]> Bytes(CancellationToken cancellationToken = default)
    {
        await BufferAsync(cancellationToken).ConfigureAwait(false);
        return await _response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the body as a stream without buffering it. Dispose the stream when done.
    /// The client timeout does not apply to reading the stream; use the cancellation token.
    /// </summary>
    public async Task<Stream> Stream(CancellationToken cancellationToken = default)
    {
        _deadline.CancelAfter(Timeout.InfiniteTimeSpan);
        try
        {
            return await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new NexarException(ErrorKind.Body, $"Failed to read the response body: {ex.Message}", Url, innerException: ex);
        }
    }

    /// <summary>
    /// Deserializes the body as JSON using the client's JSON options.
    /// </summary>
    /// <exception cref="NexarException">The body is empty, <c>null</c>, or not valid JSON for <typeparamref name="T"/>.</exception>
    public async Task<T> Json<T>(CancellationToken cancellationToken = default)
    {
        var bytes = await Bytes(cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: the response body is empty.", Url);
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(bytes, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: {ex.Message}", Url, innerException: ex);
        }

        if (value is null && Nullable.GetUnderlyingType(typeof(T)) == null)
        {
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: the response body is JSON null.", Url);
        }
        return value!;
    }

    /// <summary>
    /// Reads the whole body into memory within the request timeout.
    /// </summary>
    private async Task BufferAsync(CancellationToken cancellationToken)
    {
        if (_buffered)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
        try
        {
            await _response.Content.LoadIntoBufferAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NexarException(ErrorKind.Timeout, $"Reading the response body from {Url} timed out.", Url, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new NexarException(ErrorKind.Body, $"Failed to read the response body: {ex.Message}", Url, innerException: ex);
        }

        _buffered = true;
        _deadline.CancelAfter(Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Reads up to <see cref="MaxErrorBodyBytes"/> of the body. Failures are ignored: the status error matters more.
    /// </summary>
    private async Task<(string? Body, bool Truncated)> ReadErrorBodyAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
        try
        {
            await using var stream = await _response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            var buffer = new byte[MaxErrorBodyBytes + 1];
            var read = 0;
            int count;
            while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), linked.Token).ConfigureAwait(false)) > 0)
            {
                read += count;
            }

            var truncated = read > MaxErrorBodyBytes;
            return (GetEncoding().GetString(buffer, 0, Math.Min(read, MaxErrorBodyBytes)), truncated);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (null, false);
        }
    }

    private Encoding GetEncoding()
    {
        var charset = _response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (charset != null)
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
            }
        }
        return Encoding.UTF8;
    }

    /// <summary>Releases the response and its connection.</summary>
    public void Dispose()
    {
        _response.Dispose();
        _deadline.Dispose();
    }
}
