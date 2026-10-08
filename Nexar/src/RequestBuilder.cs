using System.Net.Http.Headers;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    private readonly Dictionary<string, string> _pathParameters = new(StringComparer.Ordinal);
    private Func<HttpContent>? _content;
    private bool _isReplayable = true;
    private TimeSpan? _timeout;
    private Version? _version;
    private HttpVersionPolicy? _versionPolicy;
    private bool? _expectContinue;
    private ContentEncoding? _compression;
    private long? _maxResponseSize;
    private IProgress<TransferProgress>? _uploadProgress;
    private IProgress<TransferProgress>? _downloadProgress;
    private CacheMode _cacheMode;
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
    /// Builds the request without sending it, so it can be inspected or signed, then sent with
    /// <see cref="NexarClient.Execute"/>.
    /// </summary>
    /// <exception cref="NexarException">The request is invalid (<see cref="ErrorKind.Builder"/>).</exception>
    public NexarRequest Build() => new(Prepare());

    /// <summary>
    /// Copies this builder, so a similar request can be sent again with changes. Returns null if the body
    /// is a stream, which can only be sent once.
    /// </summary>
    public RequestBuilder? TryClone()
    {
        if (!_isReplayable)
        {
            return null;
        }

        var clone = new RequestBuilder(_client, _method, _url)
        {
            _content = _content,
            _isReplayable = _isReplayable,
            _timeout = _timeout,
            _version = _version,
            _versionPolicy = _versionPolicy,
            _expectContinue = _expectContinue,
            _compression = _compression,
            _maxResponseSize = _maxResponseSize,
            _uploadProgress = _uploadProgress,
            _downloadProgress = _downloadProgress,
            _cacheMode = _cacheMode,
            _auth = _auth,
            _authOverridden = _authOverridden,
            _retryable = _retryable,
            _error = _error
        };
        foreach (var (name, values) in _headers)
        {
            clone._headers[name] = [.. values];
        }
        clone._query.AddRange(_query);
        foreach (var (name, value) in _pathParameters)
        {
            clone._pathParameters[name] = value;
        }
        return clone;
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
    /// Sets <c>Accept</c>, e.g. <c>.Accept("application/json", "text/csv;q=0.5")</c>.
    /// </summary>
    public RequestBuilder Accept(params string[] mediaTypes) =>
        SetParsedHeader("Accept", mediaTypes, v =>
        {
            var parsed = MediaTypeWithQualityHeaderValue.Parse(v);
            // .NET keeps an unparsable q as a plain parameter; it must be a number between 0 and 1.
            if (parsed.Parameters.Any(p => p.Name.Equals("q", StringComparison.OrdinalIgnoreCase)) && parsed.Quality is null or < 0 or > 1)
            {
                throw new FormatException($"'{v}' has an invalid quality value.");
            }
            return parsed.ToString();
        });

    /// <summary>
    /// Sets <c>Accept-Language</c>, e.g. <c>.AcceptLanguage("tr-TR", "en;q=0.8")</c>.
    /// </summary>
    public RequestBuilder AcceptLanguage(params string[] languages) =>
        SetParsedHeader("Accept-Language", languages, v => StringWithQualityHeaderValue.Parse(v).ToString());

    /// <summary>
    /// Sets <c>If-None-Match</c>: the server answers <c>304 Not Modified</c> if the resource still has one of these ETags.
    /// Accepts <c>"tag"</c>, <c>W/"tag"</c>, <c>*</c>, or a bare tag, which gets quoted.
    /// </summary>
    public RequestBuilder IfNoneMatch(params string[] etags) => SetParsedHeader("If-None-Match", etags, FormatETag);

    /// <summary>
    /// Sets <c>If-None-Match</c> from a previous response's <see cref="NexarResponse.ETag"/>.
    /// </summary>
    public RequestBuilder IfNoneMatch(EntityTagHeaderValue etag) => IfNoneMatch(etag.ToString());

    /// <summary>
    /// Sets <c>If-Match</c> for optimistic concurrency: the server answers <c>412 Precondition Failed</c>
    /// if the resource changed.
    /// </summary>
    public RequestBuilder IfMatch(params string[] etags) => SetParsedHeader("If-Match", etags, FormatETag);

    /// <summary>
    /// Sets <c>If-Match</c> from a previous response's <see cref="NexarResponse.ETag"/>.
    /// </summary>
    public RequestBuilder IfMatch(EntityTagHeaderValue etag) => IfMatch(etag.ToString());

    /// <summary>
    /// Sets <c>If-Modified-Since</c> (IMF-fixdate).
    /// </summary>
    public RequestBuilder IfModifiedSince(DateTimeOffset date) => Header("If-Modified-Since", date.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Sets <c>If-Unmodified-Since</c> (IMF-fixdate).
    /// </summary>
    public RequestBuilder IfUnmodifiedSince(DateTimeOffset date) => Header("If-Unmodified-Since", date.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Requests part of the resource: <c>Range: bytes=from-to</c>. Leave <paramref name="to"/> null to read to the end.
    /// The server answers <c>206 Partial Content</c> (see <see cref="NexarResponse.IsPartialContent"/>), or <c>200</c> if it ignores ranges.
    /// </summary>
    public RequestBuilder Range(long from, long? to = null)
    {
        Capture(() =>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(from);
            if (to < from)
            {
                throw new ArgumentOutOfRangeException(nameof(to), to, "The end of the range must not be before its start.");
            }
            _headers["Range"] = [new RangeHeaderValue(from, to).ToString()];
        });
        return this;
    }

    /// <summary>
    /// Requests the last <paramref name="length"/> bytes: <c>Range: bytes=-length</c>, e.g. the tail of a log.
    /// </summary>
    public RequestBuilder RangeSuffix(long length)
    {
        Capture(() =>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            _headers["Range"] = [new RangeHeaderValue(null, length).ToString()];
        });
        return this;
    }

    /// <summary>
    /// Sets <c>If-Range</c>: the range is honored only if the resource still has this ETag; otherwise the whole resource is sent.
    /// </summary>
    public RequestBuilder IfRange(string etag)
    {
        Capture(() => _headers["If-Range"] = [FormatETag(etag)]);
        return this;
    }

    /// <summary>
    /// Sets <c>If-Range</c> from a previous response's <see cref="NexarResponse.ETag"/>.
    /// </summary>
    public RequestBuilder IfRange(EntityTagHeaderValue etag) => IfRange(etag.ToString());

    /// <summary>
    /// Sets <c>If-Range</c>: the range is honored only if the resource was not modified after <paramref name="lastModified"/>.
    /// </summary>
    public RequestBuilder IfRange(DateTimeOffset lastModified) =>
        Header("If-Range", lastModified.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    private static string FormatETag(string etag)
    {
        if (etag == "*")
        {
            return etag;
        }
        var quoted = etag.StartsWith('"') || etag.StartsWith("W/\"", StringComparison.Ordinal) ? etag : $"\"{etag}\"";
        return EntityTagHeaderValue.Parse(quoted).ToString();
    }

    private RequestBuilder SetParsedHeader(string name, string[] values, Func<string, string> parse)
    {
        Capture(() =>
        {
            if (values.Length == 0)
            {
                throw new ArgumentException($"{name} needs at least one value.", nameof(values));
            }
            var formatted = values.Select(parse).ToList();
            foreach (var value in formatted)
            {
                HeaderValidator.Validate(name, value);
            }
            _headers[name] = formatted;
        });
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
    /// Fills the <c>{name}</c> placeholder in the URL with <paramref name="value"/>, escaped as a path segment,
    /// so values containing <c>/</c>, <c>?</c>, <c>#</c> or spaces cannot change the URL structure.
    /// </summary>
    /// <example><c>client.Get("/users/{id}/repos").Path("id", "ada lovelace")</c> sends <c>/users/ada%20lovelace/repos</c>.</example>
    public RequestBuilder Path(string name, object value)
    {
        Capture(() =>
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            var formatted = ValueEncoder.Format(value)
                ?? throw new ArgumentNullException(nameof(value), $"Path parameter '{name}' cannot be null.");
            _pathParameters[name] = formatted;
        });
        return this;
    }

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
    /// Adds query parameters from key/value pairs, e.g. a <c>Dictionary&lt;string, string&gt;</c>. Safe for Native AOT.
    /// </summary>
    public RequestBuilder Query(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _query.AddRange(values);
        return this;
    }

    /// <summary>
    /// Adds query parameters from a dictionary or an object whose properties are serialized with the client's
    /// JSON options. Arrays and nested objects follow <see cref="ClientBuilder.QueryStyle"/>.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Object)]
    [RequiresDynamicCode(AotMessages.Object)]
    public RequestBuilder Query(object values) => Query(values, _client.QueryStyle);

    /// <summary>
    /// Adds query parameters, encoding arrays and nested objects with <paramref name="style"/>,
    /// e.g. <c>.Query(new { ids = new[] { 1, 2 } }, new QueryStyle(ArrayStyle.Comma))</c>.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Object)]
    [RequiresDynamicCode(AotMessages.Object)]
    public RequestBuilder Query(object values, QueryStyle style)
    {
        Capture(() => _query.AddRange(ValueEncoder.ToPairs(values, _client.JsonOptions, style)));
        return this;
    }

    /// <summary>
    /// Sends <paramref name="value"/> as JSON using the client's JSON options.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public RequestBuilder Json<T>(T value)
    {
        Capture(() => SetJsonContent(JsonSerializer.SerializeToUtf8Bytes(value, _client.JsonOptions)));
        return this;
    }

    /// <summary>
    /// Sends <paramref name="value"/> as JSON with source-generated metadata, e.g.
    /// <c>.Json(order, AppJsonContext.Default.Order)</c>. Safe for trimming and Native AOT.
    /// </summary>
    public RequestBuilder Json<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        Capture(() => SetJsonContent(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo)));
        return this;
    }

    private void SetJsonContent(byte[] bytes) =>
        SetContent(() => CreateByteContent(bytes, "application/json; charset=utf-8"), isReplayable: true);

    /// <summary>
    /// Sends key/value pairs as <c>application/x-www-form-urlencoded</c>. Safe for Native AOT.
    /// </summary>
    public RequestBuilder Form(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var pairs = values.ToList();
        SetContent(() => new FormUrlEncodedContent(pairs), isReplayable: true);
        return this;
    }

    /// <summary>
    /// Sends <paramref name="values"/> as <c>application/x-www-form-urlencoded</c>.
    /// Accepts a dictionary or an object.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Object)]
    [RequiresDynamicCode(AotMessages.Object)]
    public RequestBuilder Form(object values) => Form(values, _client.QueryStyle);

    /// <summary>
    /// Sends <paramref name="values"/> as <c>application/x-www-form-urlencoded</c>, encoding arrays and nested
    /// objects with <paramref name="style"/>.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Object)]
    [RequiresDynamicCode(AotMessages.Object)]
    public RequestBuilder Form(object values, QueryStyle style)
    {
        Capture(() =>
        {
            var pairs = ValueEncoder.ToPairs(values, _client.JsonOptions, style);
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
    /// Sends a text body, encoded with the <c>charset</c> of <paramref name="contentType"/> (UTF-8 if it has none).
    /// </summary>
    public RequestBuilder Body(string text, string contentType = "text/plain; charset=utf-8")
    {
        Capture(() =>
        {
            var charset = MediaTypeHeaderValue.Parse(contentType).CharSet?.Trim('"');
            var encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
            var bytes = encoding.GetBytes(text);
            SetContent(() => CreateByteContent(bytes, contentType), isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Sends a text body in <paramref name="encoding"/>, with a matching <c>charset</c>,
    /// e.g. <c>.Body(xml, Encoding.Latin1, "application/xml")</c>.
    /// </summary>
    public RequestBuilder Body(string text, Encoding encoding, string mediaType = "text/plain") =>
        Body(text, $"{mediaType}; charset={encoding.WebName}");

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
    /// Sends a binary body from memory, e.g. a pooled buffer. The memory must stay unchanged until the request completes.
    /// </summary>
    public RequestBuilder Body(ReadOnlyMemory<byte> data, string contentType = "application/octet-stream")
    {
        Capture(() =>
        {
            MediaTypeHeaderValue.Parse(contentType);
            SetContent(() =>
            {
                var content = new ReadOnlyMemoryContent(data);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                return content;
            }, isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Sends a body created by <paramref name="contentFactory"/>, called once per attempt so every retry gets fresh content.
    /// </summary>
    /// <param name="contentFactory">Creates the body, e.g. from another library's serializer.</param>
    /// <param name="replayable">False if the factory cannot be called twice; the request is then never retried.</param>
    public RequestBuilder Body(Func<HttpContent> contentFactory, bool replayable = true)
    {
        ArgumentNullException.ThrowIfNull(contentFactory);
        SetContent(contentFactory, replayable);
        return this;
    }

    /// <summary>
    /// Sends a ready-made <see cref="HttpContent"/>. It is disposed after sending, so the request is never retried;
    /// use <see cref="Body(Func{HttpContent}, bool)"/> for retryable custom content.
    /// </summary>
    public RequestBuilder Body(HttpContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SetContent(() => content, isReplayable: false);
        return this;
    }

    /// <summary>
    /// Sends the file at <paramref name="path"/> as the body. The content type comes from the file extension
    /// unless given. The file is reopened for every attempt, so the request can be retried.
    /// </summary>
    public RequestBuilder File(string path, string? contentType = null)
    {
        Capture(() =>
        {
            if (!System.IO.File.Exists(path))
            {
                throw new ArgumentException($"File '{path}' does not exist.", nameof(path));
            }
            var type = contentType ?? MimeTypes.FromPath(path);
            MediaTypeHeaderValue.Parse(type);
            SetContent(() =>
            {
                var stream = System.IO.File.OpenRead(path);
                var content = new StreamContent(stream);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
                content.Headers.ContentLength = stream.Length;
                return content;
            }, isReplayable: true);
        });
        return this;
    }

    /// <summary>
    /// Downloads the response body to <paramref name="path"/> and returns its size in bytes.
    /// The body goes to <c>{path}.partial</c> first and is moved into place only when complete,
    /// so a failed download never leaves a truncated file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The destination file. It is replaced if it exists.</param>
    /// <param name="resume">
    /// Keep <c>{path}.partial</c> after a failure and continue from its end next time with a <c>Range</c> request.
    /// If the server ignores the range, the download restarts from zero. Add <see cref="IfRange(string)"/>
    /// to restart automatically when the resource changed.
    /// </param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="NexarException">The status is 4xx/5xx, or the server returned a range that does not continue the file.</exception>
    public async Task<long> DownloadTo(string path, bool resume = false, CancellationToken cancellationToken = default)
    {
        var partial = path + ".partial";
        var existing = resume && System.IO.File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existing > 0)
        {
            Range(existing);
        }

        try
        {
            using var response = await Send(cancellationToken).ConfigureAwait(false);
            await response.ErrorForStatus(cancellationToken).ConfigureAwait(false);

            var append = false;
            if (existing > 0 && response.IsPartialContent)
            {
                if (response.ContentRange?.From != existing)
                {
                    throw new NexarException(ErrorKind.Body,
                        $"Cannot resume {path}: asked for bytes from {existing}, got '{response.ContentRange}'.", response.Url);
                }
                append = true;
            }

            await response.CopyToFileAsync(partial, append, cancellationToken).ConfigureAwait(false);
        }
        catch when (!resume)
        {
            TryDelete(partial);
            throw;
        }

        System.IO.File.Move(partial, path, overwrite: true);
        return new FileInfo(path).Length;
    }

    /// <summary>
    /// Sends the request and every following page linked with <c>Link: &lt;...&gt;; rel="next"</c> (RFC 8288),
    /// yielding the items of each page, whose body is a JSON array of <typeparamref name="T"/>.
    /// Every page is sent with the same headers and authentication.
    /// </summary>
    /// <exception cref="NexarException">A page fails (status, decode, ...).</exception>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public IAsyncEnumerable<T> Paginate<T>(CancellationToken cancellationToken = default) =>
        Paginate<List<T>, T>(page => page, cancellationToken);

    /// <summary>
    /// Like <see cref="Paginate{T}(CancellationToken)"/>, with source-generated metadata for the page,
    /// e.g. <c>.Paginate(AppJsonContext.Default.ListRepo)</c>. Safe for trimming and Native AOT.
    /// </summary>
    public IAsyncEnumerable<T> Paginate<T>(JsonTypeInfo<List<T>> pageType, CancellationToken cancellationToken = default) =>
        Paginate(pageType, page => page, cancellationToken);

    /// <summary>
    /// Like <see cref="Paginate{T}(CancellationToken)"/>, for pages where the items are wrapped, e.g. <c>{ "items": [...] }</c>.
    /// </summary>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public IAsyncEnumerable<TItem> Paginate<TPage, TItem>(Func<TPage, IEnumerable<TItem>> selectItems, CancellationToken cancellationToken = default) =>
        PaginateCore((response, ct) => response.Json<TPage>(ct), selectItems, cancellationToken);

    /// <summary>
    /// Like <see cref="Paginate{TPage, TItem}(Func{TPage, IEnumerable{TItem}}, CancellationToken)"/>, with
    /// source-generated metadata for the page. Safe for trimming and Native AOT.
    /// </summary>
    public IAsyncEnumerable<TItem> Paginate<TPage, TItem>(JsonTypeInfo<TPage> pageType, Func<TPage, IEnumerable<TItem>> selectItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageType);
        return PaginateCore((response, ct) => response.Json(pageType, ct), selectItems, cancellationToken);
    }

    private async IAsyncEnumerable<TItem> PaginateCore<TPage, TItem>(
        Func<NexarResponse, CancellationToken, Task<TPage>> readPage,
        Func<TPage, IEnumerable<TItem>> selectItems,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectItems);
        var request = Prepare();
        var visited = new HashSet<Uri>();

        while (visited.Add(request.Url))
        {
            Uri? next;
            List<TItem> items;
            using (var response = await _client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false))
            {
                await response.ErrorForStatus(cancellationToken).ConfigureAwait(false);
                items = selectItems(await readPage(response, cancellationToken).ConfigureAwait(false)).ToList();
                response.Links.TryGetValue("next", out next);
            }

            foreach (var item in items)
            {
                yield return item;
            }

            if (next == null)
            {
                yield break;
            }
            request = request with { Url = next };
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
    /// Sets an <c>Idempotency-Key</c> header (a new GUID unless <paramref name="key"/> is given) and makes the request
    /// <see cref="Retryable"/>. The key is fixed when this is called, so every attempt carries the same one and the
    /// server can deduplicate a POST that is retried.
    /// </summary>
    public RequestBuilder IdempotencyKey(string? key = null, string headerName = "Idempotency-Key")
    {
        Header(headerName, key ?? Guid.NewGuid().ToString());
        return Retryable();
    }

    /// <summary>
    /// Allows (or forbids) retrying this request after a timeout or a transient status.
    /// By default only idempotent methods are retried; opt a <c>POST</c> or <c>PATCH</c> in when the server
    /// deduplicates it, for example with an <c>Idempotency-Key</c> header (see <see cref="IdempotencyKey"/>).
    /// </summary>
    public RequestBuilder Retryable(bool retryable = true)
    {
        _retryable = retryable;
        return this;
    }

    /// <summary>
    /// Sets the HTTP version to request, overriding <see cref="ClientBuilder.HttpVersion"/>.
    /// </summary>
    /// <param name="version">e.g. <see cref="System.Net.HttpVersion.Version20"/>.</param>
    /// <param name="policy">
    /// Whether a lower (default) or higher version may be negotiated, or only this one
    /// (<see cref="HttpVersionPolicy.RequestVersionExact"/>, e.g. for HTTP/3 only).
    /// </param>
    public RequestBuilder Version(Version version, HttpVersionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        _versionPolicy = policy;
        return this;
    }

    /// <summary>
    /// With <see cref="ClientBuilder.Cache"/>: always revalidates with the server instead of serving a cached copy
    /// (the cached body is still used if the server answers <c>304</c>).
    /// </summary>
    public RequestBuilder NoCache()
    {
        _cacheMode = CacheMode.Revalidate;
        return this;
    }

    /// <summary>
    /// With <see cref="ClientBuilder.Cache"/>: answers only from a fresh cached copy and never contacts the server;
    /// without one, the response is <c>504 Gateway Timeout</c> (RFC 9111 <c>only-if-cached</c>).
    /// </summary>
    public RequestBuilder OnlyIfCached()
    {
        _cacheMode = CacheMode.OnlyIfCached;
        return this;
    }

    /// <summary>
    /// Reports upload progress (bytes written to the network) as the body is sent.
    /// </summary>
    /// <example><c>.UploadProgress(new Progress&lt;TransferProgress&gt;(p =&gt; bar.Value = p.Percent ?? 0))</c></example>
    public RequestBuilder UploadProgress(IProgress<TransferProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _uploadProgress = progress;
        return this;
    }

    /// <summary>
    /// Reports download progress as the response body is read by <c>Text()</c>, <c>Bytes()</c>, <c>Json()</c>,
    /// <c>SaveTo()</c>, <c>DownloadTo()</c> or <c>Stream()</c>.
    /// </summary>
    public RequestBuilder DownloadProgress(IProgress<TransferProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _downloadProgress = progress;
        return this;
    }

    /// <summary>
    /// Compresses the body while sending it and sets <c>Content-Encoding</c>, for APIs that accept compressed
    /// uploads (telemetry, log ingestion, bulk imports). The body stays retryable.
    /// </summary>
    public RequestBuilder Compress(ContentEncoding encoding = ContentEncoding.Gzip)
    {
        _compression = encoding;
        return this;
    }

    /// <summary>
    /// Fails reading the body with <see cref="ErrorKind.Body"/> if it is larger than <paramref name="maxBytes"/>,
    /// overriding <see cref="ClientBuilder.MaxResponseSize"/>. Applies to <c>Text()</c>, <c>Bytes()</c>, <c>Json()</c>
    /// and <c>SaveTo()</c>; <c>Stream()</c> is up to the caller.
    /// </summary>
    public RequestBuilder MaxResponseSize(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _maxResponseSize = maxBytes;
        return this;
    }

    /// <summary>
    /// Sends <c>Expect: 100-continue</c> with the body, so the server can reject the request (auth, size, quota)
    /// before the body is uploaded. Overrides <see cref="ClientBuilder.ExpectContinue"/>.
    /// </summary>
    public RequestBuilder ExpectContinue(bool expect = true)
    {
        _expectContinue = expect;
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

        var template = _pathParameters.Count == 0 ? _url : UrlBuilder.ExpandPath(_url, _pathParameters);
        // Default query parameters come first; a request parameter with the same name replaces them.
        var query = _client.DefaultQuery.Count == 0
            ? _query
            : _client.DefaultQuery.Where(d => !_query.Any(q => q.Key == d.Key)).Concat(_query).ToList();
        var url = UrlBuilder.Build(_client.BaseUrl, template, query);

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

        var defaults = _client.RequestDefaults;
        var content = _content;
        if (content != null && _compression is { } encoding)
        {
            var uncompressed = content;
            content = () => new CompressedContent(uncompressed(), encoding);
        }
        if (content != null && _uploadProgress is { } upload)
        {
            // Outermost, so it counts the bytes that actually go on the wire.
            var tracked = content;
            content = () => new UploadProgressContent(tracked(), upload);
        }

        return new PreparedRequest
        {
            Method = _method,
            Url = url,
            Headers = headers,
            Content = content,
            MaxResponseSize = _maxResponseSize ?? defaults.MaxResponseSize,
            DownloadProgress = _downloadProgress,
            CacheMode = _cacheMode,
            IsReplayable = _isReplayable,
            IsIdempotent = _retryable ?? IsIdempotent(_method),
            Authenticator = authenticator,
            Timeout = _timeout,
            Version = _version ?? defaults.Version,
            VersionPolicy = _version != null ? _versionPolicy : defaults.VersionPolicy,
            ExpectContinue = _expectContinue ?? defaults.ExpectContinue
        };
    }

    internal static readonly HttpMethod QueryMethod = new("QUERY");

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options ||
        method == HttpMethod.Trace || method == HttpMethod.Put || method == HttpMethod.Delete || method == QueryMethod;

    internal RequestBuilder Fail(Exception error)
    {
        _error ??= error;
        return this;
    }

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
