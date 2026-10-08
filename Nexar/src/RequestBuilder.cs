using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nexar;

/// <summary>
/// Configures a single request through method chaining. Finish with <see cref="Send"/>.
/// </summary>
/// <remarks>
/// Errors made while building (an invalid URL, a body that cannot be serialized, ...)
/// are raised by <see cref="Send"/> as a <see cref="NexarException"/> with <see cref="ErrorKind.Builder"/>.
/// </remarks>
public sealed class RequestBuilder
{
    private readonly NexarClient _client;
    private readonly HttpMethod _method;
    private readonly string _url;
    private readonly Dictionary<string, List<string>> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _query = new();
    private Func<HttpContent>? _content;
    private bool _isReplayable = true;
    private TimeSpan? _timeout;
    private Version? _version;
    private IAuthenticator? _auth;
    private bool _authOverridden;
    private bool? _retryable;
    private Exception? _error;

    internal RequestBuilder(NexarClient client, HttpMethod method, string url)
    {
        _client = client;
        _method = method;
        _url = url;
    }

    /// <summary>
    /// Sets a header, replacing any previous value with the same name.
    /// <c>Content-*</c> headers are applied to the body.
    /// </summary>
    public RequestBuilder Header(string name, string value)
    {
        Capture(() =>
        {
            HeaderValidator.Validate(name, value);
            _headers[name] = [value];
        });
        return this;
    }

    /// <summary>
    /// Adds another value to a header without removing the existing ones,
    /// e.g. <c>.Header("Accept", "a").HeaderAppend("Accept", "b")</c>.
    /// </summary>
    public RequestBuilder HeaderAppend(string name, string value)
    {
        Capture(() =>
        {
            HeaderValidator.Validate(name, value);
            if (_headers.TryGetValue(name, out var values))
            {
                values.Add(value);
            }
            else
            {
                _headers[name] = [value];
            }
        });
        return this;
    }

    /// <summary>
    /// Sets several headers.
    /// </summary>
    public RequestBuilder Headers(IEnumerable<KeyValuePair<string, string>> headers)
    {
        foreach (var header in headers)
        {
            Header(header.Key, header.Value);
        }
        return this;
    }

    /// <summary>
    /// Authenticates this request with <paramref name="authenticator"/> instead of the client's authenticator.
    /// See <see cref="global::Nexar.Auth"/> for built-in schemes.
    /// </summary>
    public RequestBuilder Auth(IAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        _auth = authenticator;
        _authOverridden = true;
        return this;
    }

    /// <summary>
    /// Sends this request without the client's authenticator.
    /// </summary>
    public RequestBuilder NoAuth()
    {
        _auth = null;
        _authOverridden = true;
        return this;
    }

    /// <summary>
    /// Sets <c>Authorization: Bearer {token}</c>. Shortcut for <c>Auth(Auth.Bearer(token))</c>.
    /// </summary>
    public RequestBuilder BearerAuth(string token) => Auth(global::Nexar.Auth.Bearer(token));

    /// <summary>
    /// Sets <c>Authorization: Basic ...</c>. Shortcut for <c>Auth(Auth.Basic(username, password))</c>.
    /// </summary>
    public RequestBuilder BasicAuth(string username, string? password = null) => Auth(global::Nexar.Auth.Basic(username, password));

    /// <summary>
    /// Adds a query parameter. <c>null</c> values are skipped.
    /// </summary>
    public RequestBuilder Query(string key, object? value)
    {
        var formatted = ValueEncoder.Format(value);
        if (formatted != null)
        {
            _query.Add(new(key, formatted));
        }
        return this;
    }

    /// <summary>
    /// Adds query parameters from a dictionary, a sequence of key/value pairs,
    /// or an object whose properties are serialized with the client's JSON options.
    /// </summary>
    public RequestBuilder Query(object values)
    {
        Capture(() => _query.AddRange(ValueEncoder.ToPairs(values, _client.JsonOptions)));
        return this;
    }

    /// <summary>
    /// Sends <paramref name="value"/> as JSON using the client's JSON options.
    /// </summary>
    public RequestBuilder Json<T>(T value)
    {
        Capture(() =>
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _client.JsonOptions);
            SetContent(() => CreateByteContent(bytes, "application/json; charset=utf-8"), isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Sends <paramref name="values"/> as <c>application/x-www-form-urlencoded</c>.
    /// Accepts a dictionary, a sequence of key/value pairs, or an object.
    /// </summary>
    public RequestBuilder Form(object values)
    {
        Capture(() =>
        {
            var pairs = ValueEncoder.ToPairs(values, _client.JsonOptions);
            SetContent(() => new FormUrlEncodedContent(pairs), isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Sends a <c>multipart/form-data</c> body.
    /// </summary>
    public RequestBuilder Multipart(MultipartForm form)
    {
        SetContent(form.Build, form.IsReplayable);
        return this;
    }

    /// <summary>
    /// Sends a text body.
    /// </summary>
    public RequestBuilder Body(string text, string contentType = "text/plain; charset=utf-8")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Body(bytes, contentType);
    }

    /// <summary>
    /// Sends a binary body.
    /// </summary>
    public RequestBuilder Body(byte[] bytes, string contentType = "application/octet-stream")
    {
        Capture(() =>
        {
            MediaTypeHeaderValue.Parse(contentType);
            SetContent(() => CreateByteContent(bytes, contentType), isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Sends a streamed body. Requests with a stream body are never retried.
    /// </summary>
    public RequestBuilder Body(Stream stream, string contentType = "application/octet-stream")
    {
        Capture(() =>
        {
            MediaTypeHeaderValue.Parse(contentType);
            SetContent(() =>
            {
                var content = new StreamContent(stream);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                return content;
            }, isReplayable: false);
        });
        return this;
    }

    /// <summary>
    /// Overrides the client's timeout for this request.
    /// </summary>
    public RequestBuilder Timeout(TimeSpan timeout)
    {
        _timeout = ClientBuilder.ValidateTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Allows (or forbids) retrying this request after a timeout or a transient status.
    /// By default only idempotent methods are retried; opt a <c>POST</c> or <c>PATCH</c> in when the server
    /// deduplicates it, for example with an <c>Idempotency-Key</c> header.
    /// </summary>
    public RequestBuilder Retryable(bool retryable = true)
    {
        _retryable = retryable;
        return this;
    }

    /// <summary>
    /// Sets the HTTP version to request.
    /// </summary>
    public RequestBuilder Version(Version version)
    {
        _version = version;
        return this;
    }

    /// <summary>
    /// Sends the request. The returned response's body has not been read yet.
    /// </summary>
    /// <exception cref="NexarException">The request could not be built or sent.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<NexarResponse> Send(CancellationToken cancellationToken = default)
    {
        var request = Prepare();
        return await _client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private PreparedRequest Prepare()
    {
        if (_error != null)
        {
            throw new NexarException(ErrorKind.Builder, $"Invalid request: {_error.Message}", innerException: _error);
        }

        var url = UrlBuilder.Build(_client.BaseUrl, _url, _query);

        // Request headers replace default headers of the same name, all values included.
        var headers = _client.DefaultHeaders.ToDictionary(
            h => h.Key, h => (IReadOnlyList<string>)[h.Value], StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in _headers)
        {
            headers[name] = values.ToArray();
        }

        // Request-level auth wins; an explicit Authorization header also opts out of the client's authenticator.
        var authenticator = _authOverridden
            ? _auth
            : _headers.ContainsKey("Authorization") ? null : _client.Authenticator;

        return new PreparedRequest(_method, url, headers, _content, _isReplayable, _timeout, _version, authenticator,
            isIdempotent: _retryable ?? IsIdempotent(_method));
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options ||
        method == HttpMethod.Trace || method == HttpMethod.Put || method == HttpMethod.Delete;

    private void SetContent(Func<HttpContent> content, bool isReplayable)
    {
        _content = content;
        _isReplayable = isReplayable;
    }

    private void Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            _error ??= ex;
        }
    }

    private static ByteArrayContent CreateByteContent(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }
}
