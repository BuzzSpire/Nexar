using System.Net;
using System.Text.Json;

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
public sealed class NexarClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _timeout;
    private readonly RetryPolicy _retry;

    /// <summary>
    /// Creates a client with the default configuration.
    /// </summary>
    public NexarClient() : this(Builder().Build())
    {
    }

    private NexarClient(NexarClient other)
        : this(other._httpClient, other._ownsHttpClient, other.BaseUrl, other.DefaultHeaders,
            other._timeout, other._retry, other.JsonOptions, other.Authenticator)
    {
    }

    internal NexarClient(
        HttpClient httpClient,
        bool ownsHttpClient,
        Uri? baseUrl,
        IReadOnlyDictionary<string, string> defaultHeaders,
        TimeSpan timeout,
        RetryPolicy retry,
        JsonSerializerOptions jsonOptions,
        IAuthenticator? authenticator)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        BaseUrl = baseUrl;
        DefaultHeaders = defaultHeaders;
        _timeout = timeout;
        _retry = retry;
        JsonOptions = jsonOptions;
        Authenticator = authenticator;
    }

    /// <summary>
    /// Starts configuring a new client.
    /// </summary>
    public static ClientBuilder Builder() => new();

    /// <summary>The base URL relative request URLs are joined to.</summary>
    public Uri? BaseUrl { get; }

    internal IReadOnlyDictionary<string, string> DefaultHeaders { get; }

    internal JsonSerializerOptions JsonOptions { get; }

    internal IAuthenticator? Authenticator { get; }

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

    internal async Task<NexarResponse> ExecuteAsync(PreparedRequest request, CancellationToken cancellationToken)
    {
        var maxRetries = request.IsReplayable ? _retry.MaxRetries : 0;
        var timeout = request.Timeout ?? _timeout;
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
                response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, sendCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                deadline.Dispose();
                if (canRetry && request.IsIdempotent)
                {
                    await Task.Delay(Backoff(attempt++), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new NexarException(ErrorKind.Timeout, $"Request to {request.Url} timed out.", request.Url, innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                deadline.Dispose();
                var isConnectError = IsConnectError(ex);
                // A request that never got a connection never reached the server, so any method is safe to retry.
                if (canRetry && (request.IsIdempotent || isConnectError))
                {
                    await Task.Delay(Backoff(attempt++), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var kind = isConnectError ? ErrorKind.Connect : ErrorKind.Request;
                throw new NexarException(kind, $"Request to {request.Url} failed: {ex.Message}", request.Url, innerException: ex);
            }
            catch
            {
                deadline.Dispose();
                throw;
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
                continue;
            }

            if (canRetry && request.IsIdempotent && IsTransientStatus(response.StatusCode)
                && RetryDelay(attempt, response) is { } delay)
            {
                response.Dispose();
                deadline.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }

            return new NexarResponse(response, request.Url, JsonOptions, deadline);
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
        var milliseconds = _retry.Delay.TotalMilliseconds * (_retry.ExponentialBackoff ? Math.Pow(2, attempt) : 1);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, _retry.MaxDelay.TotalMilliseconds));
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
        return requested > _retry.MaxDelay ? null : requested;
    }

    private static bool IsConnectError(HttpRequestException ex) => ex.HttpRequestError is
        HttpRequestError.ConnectionError or
        HttpRequestError.NameResolutionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ProxyTunnelError;

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
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

internal sealed record RetryPolicy(int MaxRetries, TimeSpan Delay, bool ExponentialBackoff, TimeSpan MaxDelay)
{
    public static readonly RetryPolicy None = new(0, TimeSpan.Zero, false, TimeSpan.Zero);
}
