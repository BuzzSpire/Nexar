using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nexar;

/// <summary>
/// A reusable, thread-safe HTTP client. Create one and share it across your application.
/// </summary>
/// <example>
/// <code>
/// var client = NexarClient.Builder()
///     .BaseUrl("https://api.example.com")
///     .Build();
///
/// var user = await client.Get("/users/1").Send().ErrorForStatus().Json&lt;User&gt;();
/// </code>
/// </example>
public sealed partial class NexarClient : IDisposable
{
    private readonly ClientOptions _options;

    /// <summary>
    /// Creates a client with the default configuration.
    /// </summary>
    public NexarClient() : this(Builder().BuildOptions())
    {
    }

    internal NexarClient(ClientOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Starts configuring a new client.
    /// </summary>
    public static ClientBuilder Builder() => new();

    /// <summary>The base URL relative request URLs are joined to.</summary>
    public Uri? BaseUrl => _options.BaseUrl;

    internal IReadOnlyDictionary<string, string> DefaultHeaders => _options.DefaultHeaders;

    internal JsonSerializerOptions JsonOptions => _options.JsonOptions;

    internal IAuthenticator? Authenticator => _options.Authenticator;

    internal RequestDefaults RequestDefaults => _options.RequestDefaults;

    internal IReadOnlyList<KeyValuePair<string, string>> DefaultQuery => _options.DefaultQuery;

    internal IReadOnlyList<IContentSerializer> Serializers => _options.Serializers;

    /// <summary>
    /// Creates a client with different request defaults (base URL, headers, query, auth, timeout, retries, JSON
    /// options, ...) that shares this client's connection pool. Disposing it does not close the pool.
    /// </summary>
    /// <example>
    /// <code>
    /// var tenant = client.With(b => b.BaseUrl($"{baseUrl}/tenants/acme").DefaultHeader("X-Tenant", "acme"));
    /// </code>
    /// </example>
    /// <exception cref="NexarException">
    /// <paramref name="configure"/> changes handler settings (proxy, TLS, cookies, ...), which belong to this client.
    /// </exception>
    public NexarClient With(Action<ClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = ClientBuilder.Derive(_options);
        configure(builder);
        return new NexarClient(builder.BuildOptions());
    }

    internal QueryStyle QueryStyle => _options.RequestDefaults.QueryStyle;

    /// <summary>Starts a GET request.</summary>
    public RequestBuilder Get(string url) => Request(HttpMethod.Get, url);

    /// <summary>Starts a POST request.</summary>
    public RequestBuilder Post(string url) => Request(HttpMethod.Post, url);

    /// <summary>Starts a PUT request.</summary>
    public RequestBuilder Put(string url) => Request(HttpMethod.Put, url);

    /// <summary>Starts a PATCH request.</summary>
    public RequestBuilder Patch(string url) => Request(HttpMethod.Patch, url);

    /// <summary>Starts a DELETE request.</summary>
    public RequestBuilder Delete(string url) => Request(HttpMethod.Delete, url);

    /// <summary>Starts a HEAD request.</summary>
    public RequestBuilder Head(string url) => Request(HttpMethod.Head, url);

    /// <summary>Starts a request with any HTTP method.</summary>
    public RequestBuilder Request(HttpMethod method, string url) => new(this, method, url);

    /// <summary>
    /// Starts a request with a method given by name, e.g. WebDAV's <c>PROPFIND</c> or <c>MKCOL</c>.
    /// An invalid method name raises <see cref="ErrorKind.Builder"/> at <c>Send()</c>.
    /// Unknown methods are treated as non-idempotent, so they are not retried unless <see cref="RequestBuilder.Retryable"/> is used.
    /// </summary>
    public RequestBuilder Request(string method, string url)
    {
        try
        {
            return new RequestBuilder(this, new HttpMethod(method), url);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return new RequestBuilder(this, HttpMethod.Get, url).Fail(new ArgumentException($"'{method}' is not a valid HTTP method.", nameof(method), ex));
        }
    }

    /// <summary>
    /// Sends a request made with <see cref="RequestBuilder.Build"/>, with retries and authentication like <c>Send()</c>.
    /// </summary>
    public Task<NexarResponse> Execute(NexarRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(request.ToPrepared(), cancellationToken);
    }

    /// <summary>Starts an OPTIONS request.</summary>
    public RequestBuilder Options(string url) => Request(HttpMethod.Options, url);

    /// <summary>Starts a TRACE request.</summary>
    public RequestBuilder Trace(string url) => Request(HttpMethod.Trace, url);

    /// <summary>
    /// Starts a QUERY request: a safe, idempotent request that carries its query in the body
    /// (IETF draft "The HTTP QUERY Method").
    /// </summary>
    public RequestBuilder Query(string url) => Request(RequestBuilder.QueryMethod, url);

    /// <summary>
    /// Sends <paramref name="request"/> with retries and re-authentication, wrapped in a span, metrics and logs.
    /// </summary>
    internal async Task<NexarResponse> ExecuteAsync(PreparedRequest request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = request.Url;
        var redactedUrl = _options.Redactor.RedactUrl(url, request.Authenticator);
        var tags = new TagList
        {
            { "http.request.method", method },
            { "server.address", url.Host },
            { "server.port", url.Port },
            { "url.scheme", url.Scheme }
        };

        using var activity = Telemetry.ActivitySource.StartActivity(method, ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
            activity.SetTag("url.full", redactedUrl);
        }

        Telemetry.ActiveRequests.Add(1, tags);
        var started = Stopwatch.GetTimestamp();
        var attempts = new AttemptState(activity, tags, redactedUrl);
        int? status = null;
        string? errorType = null;
        try
        {
            var response = await SendThroughCacheAsync(request, attempts, cancellationToken).ConfigureAwait(false);
            response.Serializers = _options.Serializers;
            status = response.Status;
            if (status >= 400)
            {
                errorType = status.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (_options.Logger.IsEnabled(LogLevel.Information))
            {
                _options.Logger.LogInformation("HTTP {Method} {Url} responded {StatusCode} in {ElapsedMs:0.0} ms",
                    method, redactedUrl, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            return response;
        }
        catch (NexarException ex)
        {
            errorType = ex.Kind.ToString().ToLowerInvariant();
            _options.Logger.LogWarning(ex, "HTTP {Method} {Url} failed ({ErrorKind}) after {ElapsedMs:0.0} ms",
                method, redactedUrl, ex.Kind, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException)
        {
            errorType = "cancelled";
            throw;
        }
        finally
        {
            Telemetry.ActiveRequests.Add(-1, tags);

            var durationTags = tags;
            if (status != null)
            {
                durationTags.Add("http.response.status_code", status.Value);
            }
            if (errorType != null)
            {
                durationTags.Add("error.type", errorType);
            }
            Telemetry.RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, durationTags);

            if (activity != null)
            {
                if (status != null)
                {
                    activity.SetTag("http.response.status_code", status.Value);
                }
                if (attempts.Resends > 0)
                {
                    activity.SetTag("http.request.resend_count", attempts.Resends);
                }
                if (errorType != null)
                {
                    activity.SetTag("error.type", errorType);
                    activity.SetStatus(ActivityStatusCode.Error);
                }
            }
        }
    }

    private async Task<NexarResponse> SendAsync(PreparedRequest request, AttemptState attempts, CancellationToken cancellationToken)
    {
        var maxRetries = request.IsReplayable ? _options.Retry.MaxRetries : 0;
        var timeout = request.Timeout ?? _options.Timeout;
        var authenticator = request.Authenticator;
        var reauthenticated = false;
        var attempt = 0;

        while (true)
        {
            var canRetry = attempt < maxRetries;
            using var message = CreateMessage(request);
            if (authenticator != null)
            {
                await AuthenticateAsync(authenticator, message, request.Url, cancellationToken).ConfigureAwait(false);
            }
            foreach (var hook in _options.RequestHooks)
            {
                await RunHookAsync(() => hook(message, cancellationToken), "OnRequest", request.Url).ConfigureAwait(false);
            }
            LogHeaders("Request", message.Headers, message.Content?.Headers, authenticator);

            using var lease = await AcquirePermitAsync(request, cancellationToken).ConfigureAwait(false);
            var circuit = _options.CircuitBreaker;
            circuit?.Enter(request.Url);

            // The deadline also covers reading the body, so on success it is handed over to the response.
            var deadline = new CancellationTokenSource();
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                deadline.CancelAfter(timeout);
            }

            HttpResponseMessage response;
            try
            {
                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                response = await _options.HttpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, sendCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                deadline.Dispose();
                circuit?.Record(request.Url, CircuitOutcome.Failure);
                if (canRetry && ShouldRetry(request, attempt, response: null, ex, retriedByDefault: true, alwaysSafe: false))
                {
                    await ResendAfterAsync(attempts, request, "timeout", Backoff(attempt++), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new NexarException(ErrorKind.Timeout, $"Request to {request.Url} timed out.", request.Url, innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                deadline.Dispose();
                circuit?.Record(request.Url, CircuitOutcome.Failure);
                var isConnectError = IsConnectError(ex);
                // A request that never got a connection never reached the server, so any method is safe to retry.
                if (canRetry && ShouldRetry(request, attempt, response: null, ex, retriedByDefault: true, alwaysSafe: isConnectError))
                {
                    await ResendAfterAsync(attempts, request, isConnectError ? "connect" : "request", Backoff(attempt++), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var kind = isConnectError ? ErrorKind.Connect : ErrorKind.Request;
                throw new NexarException(kind, $"Request to {request.Url} failed: {ex.Message}", request.Url, innerException: ex);
            }
            catch
            {
                deadline.Dispose();
                circuit?.Record(request.Url, CircuitOutcome.Neutral);
                throw;
            }

            circuit?.Record(request.Url, (int)response.StatusCode >= 500 ? CircuitOutcome.Failure : CircuitOutcome.Success);
            LogHeaders("Response", response.Headers, response.Content.Headers, authenticator);
            foreach (var hook in _options.ResponseHooks)
            {
                try
                {
                    await RunHookAsync(() => hook(response, cancellationToken), "OnResponse", request.Url).ConfigureAwait(false);
                }
                catch
                {
                    response.Dispose();
                    deadline.Dispose();
                    throw;
                }
            }

            // The default handler returns the last 3xx when it stops following redirects.
            if (_options.RedirectLimit is { } limit && IsFollowableRedirect(response))
            {
                var location = response.Headers.Location;
                var finalUrl = response.RequestMessage?.RequestUri ?? request.Url;
                response.Dispose();
                deadline.Dispose();
                throw new NexarException(ErrorKind.Redirect,
                    $"Redirect from {finalUrl} to {location} was not followed: more than {limit} redirects, or a redirect from HTTPS to HTTP.",
                    finalUrl);
            }

            // One re-send after a 401 if the authenticator can fix it; it does not use up a retry.
            if (response.StatusCode == HttpStatusCode.Unauthorized
                && authenticator != null
                && request.IsReplayable
                && !reauthenticated
                && await OnUnauthorizedAsync(authenticator, response, request.Url, cancellationToken).ConfigureAwait(false))
            {
                reauthenticated = true;
                response.Dispose();
                deadline.Dispose();
                await ResendAfterAsync(attempts, request, "unauthorized", TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (canRetry && (int)response.StatusCode >= 300
                && ShouldRetry(request, attempt, response, exception: null, IsTransientStatus(response.StatusCode), alwaysSafe: false)
                && RetryDelay(attempt, response) is { } delay)
            {
                var reason = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                response.Dispose();
                deadline.Dispose();
                await ResendAfterAsync(attempts, request, reason, delay, cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }

            if (request.DownloadProgress is { } progress)
            {
                response.Content = new DownloadProgressContent(response.Content, progress);
            }
            return new NexarResponse(response, request.Url, JsonOptions, deadline, request.MaxResponseSize);
        }
    }

    /// <summary>
    /// Records a re-send (span event, metric, debug log) and waits <paramref name="delay"/>.
    /// </summary>
    private async Task ResendAfterAsync(AttemptState attempts, PreparedRequest request, string reason, TimeSpan delay, CancellationToken cancellationToken)
    {
        attempts.Resends++;
        foreach (var callback in _options.Retry.Callbacks)
        {
            try
            {
                callback(new RetryEvent(attempts.Resends, reason, delay, request.Method, request.Url));
            }
            catch (Exception ex)
            {
                _options.Logger.LogWarning(ex, "An OnRetry callback failed");
            }
        }
        attempts.Activity?.AddEvent(new ActivityEvent("nexar.resend", tags: new ActivityTagsCollection
        {
            ["nexar.resend.reason"] = reason,
            ["nexar.resend.count"] = attempts.Resends,
            ["nexar.resend.delay_ms"] = delay.TotalMilliseconds
        }));

        var tags = attempts.Tags;
        tags.Add("nexar.resend.reason", reason);
        Telemetry.Resends.Add(1, tags);

        if (_options.Logger.IsEnabled(LogLevel.Debug))
        {
            _options.Logger.LogDebug("Re-sending {Url} ({Reason}) in {DelayMs:0} ms, resend #{Count}",
                attempts.RedactedUrl, reason, delay.TotalMilliseconds, attempts.Resends);
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogHeaders(string direction, System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpHeaders? contentHeaders, IAuthenticator? authenticator)
    {
        if (!_options.Logger.IsEnabled(LogLevel.Trace))
        {
            return;
        }

        var all = contentHeaders == null ? headers : headers.Concat(contentHeaders);
        var text = string.Join("; ", all.Select(h => $"{h.Key}: {_options.Redactor.RedactHeader(h.Key, h.Value, authenticator)}"));
        _options.Logger.LogTrace("{Direction} headers: {Headers}", direction, text);
    }

    /// <summary>
    /// Waits for a permit from the client's rate limiter, if any. The lease is held while the attempt is sent.
    /// </summary>
    private async ValueTask<System.Threading.RateLimiting.RateLimitLease?> AcquirePermitAsync(PreparedRequest request, CancellationToken cancellationToken)
    {
        if (_options.RateLimiter is not { } limiter)
        {
            return null;
        }

        var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired)
        {
            return lease;
        }

        lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter);
        lease.Dispose();
        throw new NexarException(ErrorKind.RateLimited, $"The client rate limiter refused the request to {request.Url}.", request.Url)
        {
            RetryAfter = retryAfter == default ? null : retryAfter
        };
    }

    private static async ValueTask RunHookAsync(Func<ValueTask> hook, string name, Uri url)
    {
        try
        {
            await hook().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not NexarException and not OperationCanceledException)
        {
            throw new NexarException(ErrorKind.Request, $"An {name} hook failed for {url}: {ex.Message}", url, innerException: ex);
        }
    }

    private static HttpRequestMessage CreateMessage(PreparedRequest request)
    {
        try
        {
            return request.CreateMessage();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // e.g. a File() body that was deleted after the request was built
            throw new NexarException(ErrorKind.Builder, $"Could not create the request body: {ex.Message}", request.Url, innerException: ex);
        }
    }

    private static async ValueTask AuthenticateAsync(IAuthenticator authenticator, HttpRequestMessage message, Uri url, CancellationToken cancellationToken)
    {
        try
        {
            await authenticator.AuthenticateAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not NexarException && ex is not OperationCanceledException)
        {
            throw new NexarException(ErrorKind.Auth, $"Authentication for {url} failed: {ex.Message}", url, innerException: ex);
        }
    }

    private static async ValueTask<bool> OnUnauthorizedAsync(IAuthenticator authenticator, HttpResponseMessage response, Uri url, CancellationToken cancellationToken)
    {
        try
        {
            return await authenticator.OnUnauthorizedAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not NexarException && ex is not OperationCanceledException)
        {
            response.Dispose();
            throw new NexarException(ErrorKind.Auth, $"Authentication for {url} failed: {ex.Message}", url, innerException: ex);
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        var retry = _options.Retry;
        var milliseconds = retry.Delay.TotalMilliseconds * (retry.ExponentialBackoff ? Math.Pow(2, attempt) : 1);
        milliseconds = Math.Min(milliseconds, retry.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(retry.Jitter ? Random.Shared.NextDouble() * milliseconds : milliseconds);
    }

    /// <summary>
    /// Combines Nexar's default decision with <see cref="ClientBuilder.RetryWhen"/>. Non-idempotent requests are only
    /// retried when they opted in, or when the failure is known not to have reached the server.
    /// </summary>
    private bool ShouldRetry(PreparedRequest request, int attempt, HttpResponseMessage? response, Exception? exception,
        bool retriedByDefault, bool alwaysSafe)
    {
        var decision = retriedByDefault;
        if (_options.Retry.Condition is { } condition)
        {
            decision = condition(new RetryContext(attempt + 1, request.Method, request.Url, response, exception, retriedByDefault)) ?? retriedByDefault;
        }
        return decision && (request.IsIdempotent || alwaysSafe);
    }

    /// <summary>
    /// The delay before retrying <paramref name="response"/>, honoring <c>Retry-After</c>.
    /// Null when the server asks to wait longer than <see cref="RetryPolicy.MaxDelay"/>.
    /// </summary>
    private TimeSpan? RetryDelay(int attempt, HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null)
        {
            return Backoff(attempt);
        }

        var requested = retryAfter.Delta
            ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : Backoff(attempt));
        if (requested < TimeSpan.Zero)
        {
            requested = TimeSpan.Zero;
        }
        return requested > _options.Retry.MaxDelay ? null : requested;
    }

    private static bool IsConnectError(HttpRequestException ex) => ex.HttpRequestError is
        HttpRequestError.ConnectionError or
        HttpRequestError.NameResolutionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ProxyTunnelError;

    private static bool IsFollowableRedirect(HttpResponseMessage response) =>
        response.Headers.Location != null && response.StatusCode is
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool IsTransientStatus(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> unless it was supplied through
    /// <see cref="ClientBuilder.HttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        if (_options.OwnsHttpClient)
        {
            _options.HttpClient.Dispose();
        }
    }

    private sealed class AttemptState(Activity? activity, TagList tags, string redactedUrl)
    {
        public Activity? Activity { get; } = activity;

        public TagList Tags { get; } = tags;

        public string RedactedUrl { get; } = redactedUrl;

        public int Resends { get; set; }
    }
}

internal sealed record RetryPolicy(int MaxRetries, TimeSpan Delay, bool ExponentialBackoff, TimeSpan MaxDelay)
{
    public static readonly RetryPolicy None = new(0, TimeSpan.Zero, false, TimeSpan.Zero);

    /// <summary>Spread each computed backoff randomly between zero and its value ("full jitter").</summary>
    public bool Jitter { get; init; }

    /// <summary>Adds (true) or vetoes (false) retries; null keeps Nexar's decision.</summary>
    public Func<RetryContext, bool?>? Condition { get; init; }

    public IReadOnlyList<Action<RetryEvent>> Callbacks { get; init; } = [];
}

/// <summary>
/// Everything a <see cref="NexarClient"/> is configured with, produced by <see cref="ClientBuilder"/>.
/// </summary>
internal sealed record ClientOptions(
    HttpClient HttpClient,
    bool OwnsHttpClient,
    Uri? BaseUrl,
    IReadOnlyDictionary<string, string> DefaultHeaders,
    TimeSpan Timeout,
    RetryPolicy Retry,
    JsonSerializerOptions JsonOptions,
    IAuthenticator? Authenticator,
    ILogger Logger,
    Redactor Redactor,
    int? RedirectLimit,
    RequestDefaults RequestDefaults,
    System.Threading.RateLimiting.RateLimiter? RateLimiter,
    IHttpCache? Cache,
    TimeProvider CacheClock,
    IReadOnlyList<KeyValuePair<string, string>> DefaultQuery,
    IReadOnlyList<IContentSerializer> Serializers,
    IReadOnlyList<Func<HttpRequestMessage, CancellationToken, ValueTask>> RequestHooks,
    IReadOnlyList<Func<HttpResponseMessage, CancellationToken, ValueTask>> ResponseHooks,
    CircuitBreaker? CircuitBreaker);

/// <summary>
/// Client-wide defaults that individual requests can override.
/// </summary>
internal sealed record RequestDefaults(
    Version? Version,
    HttpVersionPolicy? VersionPolicy,
    bool ExpectContinue,
    long? MaxResponseSize,
    QueryStyle QueryStyle)
{
    public static readonly RequestDefaults None = new(null, null, false, null, QueryStyle.Default);
}
