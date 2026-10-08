using System.Text.Json;

namespace Nexar;

/// <summary>
/// Configures and builds a <see cref="NexarClient"/>.
/// </summary>
public sealed class ClientBuilder
{
    private string? _baseUrl;
    private TimeSpan _timeout = TimeSpan.FromSeconds(100);
    private readonly Dictionary<string, string> _defaultHeaders = new(StringComparer.OrdinalIgnoreCase);
    private JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private RetryPolicy _retry = RetryPolicy.None;
    private bool _acceptInvalidCerts;
    private System.Net.Http.HttpMessageHandler? _primaryHandler;
    private readonly List<DelegatingHandler> _handlers = new();
    private System.Net.Http.HttpClient? _httpClient;
    private IAuthenticator? _authenticator;
    private System.Net.ICredentials? _credentials;
    private bool _preAuthenticate;
    private System.Net.DecompressionMethods? _decompression;

    internal ClientBuilder()
    {
    }

    /// <summary>
    /// Sets the base URL that relative request URLs are joined to.
    /// </summary>
    public ClientBuilder BaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    /// <summary>
    /// Sets the default timeout for receiving response headers. Defaults to 100 seconds.
    /// Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public ClientBuilder Timeout(TimeSpan timeout)
    {
        _timeout = ValidateTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Adds a header sent with every request. A header set on a request overrides it.
    /// </summary>
    public ClientBuilder DefaultHeader(string name, string value)
    {
        _defaultHeaders[name] = value;
        return this;
    }

    /// <summary>
    /// Adds headers sent with every request.
    /// </summary>
    public ClientBuilder DefaultHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        foreach (var header in headers)
        {
            _defaultHeaders[header.Key] = header.Value;
        }
        return this;
    }

    /// <summary>
    /// Sets the <c>User-Agent</c> header sent with every request.
    /// </summary>
    public ClientBuilder UserAgent(string userAgent) => DefaultHeader("User-Agent", userAgent);

    /// <summary>
    /// Authenticates every request with <paramref name="authenticator"/>.
    /// See <see cref="Nexar.Auth"/> for built-in schemes. Requests can override it with
    /// <see cref="RequestBuilder.Auth"/> or opt out with <see cref="RequestBuilder.NoAuth"/>.
    /// </summary>
    public ClientBuilder Auth(IAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        _authenticator = authenticator;
        return this;
    }

    /// <summary>
    /// Uses platform credentials for NTLM, Negotiate (Kerberos), Digest or Basic challenges,
    /// handled by the default <see cref="SocketsHttpHandler"/>.
    /// </summary>
    /// <param name="credentials">For example <see cref="System.Net.CredentialCache.DefaultCredentials"/> or a <see cref="System.Net.NetworkCredential"/>.</param>
    /// <param name="preAuthenticate">Send credentials up front on later requests to the same host.</param>
    public ClientBuilder Credentials(System.Net.ICredentials credentials, bool preAuthenticate = false)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        _credentials = credentials;
        _preAuthenticate = preAuthenticate;
        return this;
    }

    /// <summary>
    /// Replaces the JSON options. Defaults to <see cref="JsonSerializerDefaults.Web"/>.
    /// </summary>
    public ClientBuilder JsonOptions(JsonSerializerOptions options)
    {
        _jsonOptions = new JsonSerializerOptions(options);
        return this;
    }

    /// <summary>
    /// Adjusts the JSON options.
    /// </summary>
    public ClientBuilder JsonOptions(Action<JsonSerializerOptions> configure)
    {
        configure(_jsonOptions);
        return this;
    }

    /// <summary>
    /// Retries requests that fail with a connection error, a timeout, or a
    /// 408, 429, 500, 502, 503 or 504 status.
    /// </summary>
    /// <remarks>
    /// Only idempotent methods (GET, HEAD, OPTIONS, TRACE, PUT, DELETE) are retried, except after a failed
    /// connection, which never reached the server. Use <see cref="RequestBuilder.Retryable"/> to opt other
    /// requests in. Requests with a <see cref="Stream"/> body are never retried. A <c>Retry-After</c> header
    /// replaces the computed delay; if it asks for more than <paramref name="maxDelay"/>, the response is returned.
    /// </remarks>
    /// <param name="maxRetries">Number of retries after the first attempt.</param>
    /// <param name="delay">Delay before the first retry. Defaults to 1 second.</param>
    /// <param name="exponentialBackoff">Doubles the delay after each retry.</param>
    /// <param name="maxDelay">Upper bound for any single delay. Defaults to 30 seconds.</param>
    public ClientBuilder Retry(int maxRetries, TimeSpan? delay = null, bool exponentialBackoff = true, TimeSpan? maxDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        var retryDelay = delay ?? TimeSpan.FromSeconds(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero, nameof(delay));
        var retryMaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryMaxDelay, TimeSpan.Zero, nameof(maxDelay));
        _retry = new RetryPolicy(maxRetries, retryDelay, exponentialBackoff, retryMaxDelay);
        return this;
    }

    /// <summary>
    /// Sets which encodings the default handler asks for and decodes. Defaults to gzip, deflate and Brotli.
    /// </summary>
    public ClientBuilder Decompression(System.Net.DecompressionMethods methods)
    {
        _decompression = methods;
        return this;
    }

    /// <summary>
    /// Disables TLS certificate validation. Only use this for local development.
    /// </summary>
    public ClientBuilder DangerAcceptInvalidCerts(bool accept = true)
    {
        _acceptInvalidCerts = accept;
        return this;
    }

    /// <summary>
    /// Uses a custom primary handler instead of the default <see cref="SocketsHttpHandler"/>.
    /// </summary>
    public ClientBuilder HttpMessageHandler(System.Net.Http.HttpMessageHandler handler)
    {
        _primaryHandler = handler;
        return this;
    }

    /// <summary>
    /// Adds a <see cref="DelegatingHandler"/>. Handlers run in the order they are added.
    /// </summary>
    public ClientBuilder AddHandler(DelegatingHandler handler)
    {
        _handlers.Add(handler);
        return this;
    }

    /// <summary>
    /// Uses an existing <see cref="System.Net.Http.HttpClient"/>, for example one from <c>IHttpClientFactory</c>.
    /// Nexar never disposes it.
    /// </summary>
    public ClientBuilder HttpClient(System.Net.Http.HttpClient httpClient)
    {
        _httpClient = httpClient;
        return this;
    }

    /// <summary>
    /// Builds the client.
    /// </summary>
    /// <exception cref="NexarException">The configuration is invalid.</exception>
    public NexarClient Build()
    {
        Uri? baseUrl = null;
        if (_baseUrl != null && !UrlBuilder.TryParseHttpUrl(_baseUrl, out baseUrl))
        {
            throw new NexarException(ErrorKind.Builder, $"Base URL '{_baseUrl}' is not an absolute http(s) URL.");
        }

        var configuresDefaultHandler = _acceptInvalidCerts || _credentials != null || _decompression != null;

        if (_httpClient != null && (_primaryHandler != null || _handlers.Count > 0 || configuresDefaultHandler))
        {
            throw new NexarException(
                ErrorKind.Builder,
                "HttpClient() cannot be combined with HttpMessageHandler(), AddHandler(), DangerAcceptInvalidCerts(), Credentials() or Decompression().");
        }

        if (_primaryHandler != null && configuresDefaultHandler)
        {
            throw new NexarException(
                ErrorKind.Builder,
                "HttpMessageHandler() cannot be combined with DangerAcceptInvalidCerts(), Credentials() or Decompression(); configure your handler directly.");
        }

        var jsonOptions = new JsonSerializerOptions(_jsonOptions);
        jsonOptions.MakeReadOnly(populateMissingResolver: true);

        var ownsHttpClient = _httpClient == null;
        var httpClient = _httpClient ?? new System.Net.Http.HttpClient(BuildHandlerChain())
        {
            // Timeouts are enforced per request so they can be told apart from cancellation.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        return new NexarClient(
            httpClient,
            ownsHttpClient,
            baseUrl,
            new Dictionary<string, string>(_defaultHeaders, StringComparer.OrdinalIgnoreCase),
            _timeout,
            _retry,
            jsonOptions,
            _authenticator);
    }

    private System.Net.Http.HttpMessageHandler BuildHandlerChain()
    {
        var handler = _primaryHandler ?? CreateDefaultHandler();

        for (var i = _handlers.Count - 1; i >= 0; i--)
        {
            _handlers[i].InnerHandler = handler;
            handler = _handlers[i];
        }

        return handler;
    }

    internal SocketsHttpHandler CreateDefaultHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = _decompression ?? System.Net.DecompressionMethods.All
        };
        if (_acceptInvalidCerts)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }
        if (_credentials != null)
        {
            handler.Credentials = _credentials;
            handler.PreAuthenticate = _preAuthenticate;
        }
        return handler;
    }

    internal static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive or InfiniteTimeSpan.");
        }
        return timeout;
    }
}
