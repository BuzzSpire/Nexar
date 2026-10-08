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
        var message = $"HTTP {Status} ({ReasonPhrase ?? StatusCode.ToString()}) for {Url}";
        Dispose();

        throw new NexarException(ErrorKind.Status, message, Url, StatusCode)
        {
            ResponseBody = body,
            IsResponseBodyTruncated = truncated,
            ResponseHeaders = headers,
            JsonOptions = _jsonOptions
        };
    }

    /// <summary>
    /// Reads the body as text, using the charset from <c>Content-Type</c>.
    /// </summary>
    public async Task<string> Text(CancellationToken cancellationToken = default)
    {
        await BufferAsync(cancellationToken).ConfigureAwait(false);
        return await _response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

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
