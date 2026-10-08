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
    private System.Net.Http.HttpMessageHandler? _primaryHandler;
    private readonly List<DelegatingHandler> _handlers = new();
    private System.Net.Http.HttpClient? _httpClient;
    private IAuthenticator? _authenticator;
    private RedirectPolicy _redirects = RedirectPolicy.Default;
    private RequestDefaults _requestDefaults = RequestDefaults.None;
    private System.Threading.RateLimiting.RateLimiter? _rateLimiter;
    private IHttpCache? _cache;
    private TimeProvider _cacheClock = TimeProvider.System;

    // Settings for the default SocketsHttpHandler, keyed by the builder method that made them,
    // so they can be named when they conflict with HttpMessageHandler() or HttpClient().
    private readonly Dictionary<string, Action<SocketsHttpHandler>> _handlerSettings = new();
    private bool _acceptInvalidCerts;
    private System.Net.WebProxy? _proxy;
    private readonly List<string> _proxyBypass = new();
    private readonly List<System.Security.Cryptography.X509Certificates.X509Certificate2> _clientCertificates = new();
    private readonly List<System.Security.Cryptography.X509Certificates.X509Certificate2> _rootCertificates = new();
    private ConnectTarget? _connectTarget;
    private readonly Dictionary<string, System.Net.IPAddress[]> _resolve = new(StringComparer.OrdinalIgnoreCase);
    private System.Net.IPAddress? _localAddress;
    private Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private readonly List<string> _redactHeaders = [.. Redactor.DefaultHeaders];
    private readonly List<string> _redactQueryParameters = [.. Redactor.DefaultQueryParameters];
    private readonly List<KeyValuePair<string, string>> _defaultQuery = new();
    private readonly List<IContentSerializer> _serializers = new();
    private ClientOptions? _parent;

    internal ClientBuilder()
    {
    }

    /// <summary>
    /// A builder for <see cref="NexarClient.With"/>: starts from the parent's settings and reuses its connection pool.
    /// </summary>
    internal static ClientBuilder Derive(ClientOptions parent)
    {
        var builder = new ClientBuilder
        {
            _parent = parent,
            _baseUrl = parent.BaseUrl?.AbsoluteUri,
            _timeout = parent.Timeout,
            _jsonOptions = new JsonSerializerOptions(parent.JsonOptions),
            _retry = parent.Retry,
            _authenticator = parent.Authenticator,
            _logger = parent.Logger,
            _requestDefaults = parent.RequestDefaults,
            _rateLimiter = parent.RateLimiter,
            _cache = parent.Cache,
            _cacheClock = parent.CacheClock
        };
        foreach (var (name, value) in parent.DefaultHeaders)
        {
            builder._defaultHeaders[name] = value;
        }
        builder._defaultQuery.AddRange(parent.DefaultQuery);
        builder._serializers.AddRange(parent.Serializers);
        builder._redactHeaders.Clear();
        builder._redactHeaders.AddRange(parent.Redactor.Headers);
        builder._redactQueryParameters.Clear();
        builder._redactQueryParameters.AddRange(parent.Redactor.QueryParameters);
        return builder;
    }

    /// <summary>
    /// Registers a body format. The first registered serializer is the default for <see cref="RequestBuilder.Serialized"/>,
    /// and <c>As&lt;T&gt;()</c> picks the one that reads the response's <c>Content-Type</c>. A serializer for the same
    /// media type replaces an earlier one.
    /// </summary>
    /// <example><c>.Serializer(XmlContentSerializer.Default)</c></example>
    public ClientBuilder Serializer(IContentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var index = _serializers.FindIndex(s => s.MediaType.Equals(serializer.MediaType, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _serializers[index] = serializer;
        }
        else
        {
            _serializers.Add(serializer);
        }
        return this;
    }

    /// <summary>
    /// Adds a query parameter sent with every request, e.g. <c>.DefaultQuery("api-version", "2024-05-01")</c>.
    /// A request parameter with the same name replaces it.
    /// </summary>
    public ClientBuilder DefaultQuery(string name, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var formatted = ValueEncoder.Format(value) ?? throw new ArgumentNullException(nameof(value));
        _defaultQuery.RemoveAll(q => q.Key == name);
        _defaultQuery.Add(new(name, formatted));
        return this;
    }

    /// <summary>
    /// Logs requests through <paramref name="logger"/>: the request line, status and duration at Information,
    /// re-sends at Debug, and headers at Trace. Secrets are redacted (see <see cref="RedactHeaders"/>).
    /// </summary>
    public ClientBuilder Logger(Microsoft.Extensions.Logging.ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Adds header names whose values never appear in logs or traces. <c>Authorization</c>, <c>Cookie</c>,
    /// <c>Set-Cookie</c>, <c>Proxy-Authorization</c> and API key headers set through <see cref="Nexar.Auth"/> are always redacted.
    /// </summary>
    public ClientBuilder RedactHeaders(params string[] names)
    {
        _redactHeaders.AddRange(names);
        return this;
    }

    /// <summary>
    /// Adds query parameter names whose values are replaced by <c>REDACTED</c> in logs, traces and metrics.
    /// Common names such as <c>api_key</c>, <c>access_token</c> and <c>client_secret</c> are redacted by default.
    /// </summary>
    public ClientBuilder RedactQueryParameters(params string[] names)
    {
        _redactQueryParameters.AddRange(names);
        return this;
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
        return Configure(nameof(Credentials), h =>
        {
            h.Credentials = credentials;
            h.PreAuthenticate = preAuthenticate;
        });
    }

    /// <summary>
    /// Sets how redirects are followed. Defaults to <see cref="RedirectPolicy.Default"/> (up to 10 hops).
    /// When the limit is exceeded, <c>Send()</c> throws <see cref="ErrorKind.Redirect"/>.
    /// Redirects from HTTPS to HTTP are never followed.
    /// </summary>
    public ClientBuilder Redirects(RedirectPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _redirects = policy;
        return Configure(nameof(Redirects), _ => { });
    }

    /// <summary>
    /// Keeps cookies from <c>Set-Cookie</c> responses and sends them on later requests to the same site.
    /// Without this, the client is stateless and only sends a <c>Cookie</c> header you set yourself.
    /// </summary>
    /// <param name="cookies">A jar to inspect or pre-fill; a new one is used if null.</param>
    public ClientBuilder CookieStore(System.Net.CookieContainer? cookies = null)
    {
        var container = cookies ?? new System.Net.CookieContainer();
        return Configure(nameof(CookieStore), h =>
        {
            h.UseCookies = true;
            h.CookieContainer = container;
        });
    }

    /// <summary>
    /// Sends requests through the proxy at <paramref name="url"/> (<c>http</c>, <c>https</c>, <c>socks4</c>,
    /// <c>socks4a</c> or <c>socks5</c>). Without this, the system proxy and the <c>HTTP(S)_PROXY</c> /
    /// <c>NO_PROXY</c> environment variables are used.
    /// </summary>
    /// <param name="url">The proxy URL, e.g. <c>http://proxy.local:8080</c>.</param>
    /// <param name="credentials">Credentials for proxies that answer <c>407 Proxy Authentication Required</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute URL with a supported scheme.</exception>
    public ClientBuilder Proxy(string url, System.Net.ICredentials? credentials = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "socks4" or "socks4a" or "socks5"))
        {
            throw new ArgumentException($"'{url}' is not a proxy URL (http, https, socks4, socks4a or socks5).", nameof(url));
        }

        _proxy = new System.Net.WebProxy(uri) { Credentials = credentials };
        return ApplyProxy();
    }

    /// <summary>
    /// Hosts that skip the proxy set with <see cref="Proxy"/>. A leading <c>*</c> matches any prefix,
    /// e.g. <c>*.internal.example.com</c>.
    /// </summary>
    public ClientBuilder ProxyBypass(params string[] hosts)
    {
        _proxyBypass.AddRange(hosts);
        return _proxy == null ? this : ApplyProxy();
    }

    /// <summary>
    /// Connects directly, ignoring the system proxy and proxy environment variables.
    /// </summary>
    public ClientBuilder NoProxy()
    {
        _proxy = null;
        return Configure("Proxy", h => h.UseProxy = false);
    }

    private ClientBuilder ApplyProxy()
    {
        var proxy = _proxy!;
        // WebProxy matches these regexes against "scheme://host[:port]".
        proxy.BypassList = _proxyBypass
            .Select(host => @"^(?:[a-z][a-z0-9+.\-]*://)?" + System.Text.RegularExpressions.Regex.Escape(host).Replace("\\*", ".*") + @"(?::\d+)?$")
            .ToArray();
        return Configure("Proxy", h =>
        {
            h.UseProxy = true;
            h.Proxy = proxy;
        });
    }

    /// <summary>
    /// Presents <paramref name="certificate"/> (with its private key) to servers that ask for a client certificate (mTLS).
    /// Call it more than once to offer several; the one issued by a CA the server accepts is preferred.
    /// </summary>
    public ClientBuilder ClientCertificate(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("A client certificate needs its private key.", nameof(certificate));
        }
        _clientCertificates.Add(certificate);
        return Configure(nameof(ClientCertificate), h =>
        {
            h.SslOptions.ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection(_clientCertificates.ToArray());
            h.SslOptions.LocalCertificateSelectionCallback = SelectClientCertificate;
        });
    }

    /// <summary>
    /// Also trusts server certificates issued by <paramref name="certificate"/>, e.g. a private company CA,
    /// in addition to the system trust store. Host names are still checked.
    /// </summary>
    public ClientBuilder AddRootCertificate(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _rootCertificates.Add(certificate);
        return Configure(nameof(AddRootCertificate), h => h.SslOptions.RemoteCertificateValidationCallback = ValidateWithCustomRoots);
    }

    /// <summary>
    /// Refuses TLS versions older than <paramref name="version"/>: <see cref="System.Security.Authentication.SslProtocols.Tls12"/>
    /// or <see cref="System.Security.Authentication.SslProtocols.Tls13"/>.
    /// </summary>
    public ClientBuilder MinTlsVersion(System.Security.Authentication.SslProtocols version)
    {
        var enabled = version switch
        {
            System.Security.Authentication.SslProtocols.Tls12 => System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            System.Security.Authentication.SslProtocols.Tls13 => System.Security.Authentication.SslProtocols.Tls13,
            _ => throw new ArgumentException("The minimum TLS version must be Tls12 or Tls13.", nameof(version))
        };
        return Configure(nameof(MinTlsVersion), h => h.SslOptions.EnabledSslProtocols = enabled);
    }

    private System.Security.Cryptography.X509Certificates.X509Certificate SelectClientCertificate(
        object sender, string targetHost,
        System.Security.Cryptography.X509Certificates.X509CertificateCollection localCertificates,
        System.Security.Cryptography.X509Certificates.X509Certificate? remoteCertificate, string[] acceptableIssuers)
    {
        // Prefer a certificate from an issuer the server named; otherwise present the first one anyway,
        // since servers often send an empty or unrelated issuer list.
        return _clientCertificates.FirstOrDefault(c => acceptableIssuers.Contains(c.Issuer, StringComparer.OrdinalIgnoreCase))
            ?? _clientCertificates[0];
    }

    private bool ValidateWithCustomRoots(
        object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain, System.Net.Security.SslPolicyErrors errors)
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
        {
            return true;
        }
        // Only an untrusted chain can be fixed by extra roots; a wrong host name or a missing certificate cannot.
        if (certificate == null || errors != System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        using var custom = new System.Security.Cryptography.X509Certificates.X509Chain();
        custom.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(_rootCertificates.ToArray());
        custom.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
        if (chain != null)
        {
            foreach (var element in chain.ChainElements)
            {
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }
        using var leaf = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate);
        return custom.Build(leaf);
    }

    /// <summary>
    /// Limits how long establishing a connection (TCP connect and TLS handshake) may take, separately from
    /// <see cref="Timeout"/>, so unreachable hosts fail fast while slow responses are still allowed.
    /// A connect timeout raises <see cref="ErrorKind.Timeout"/>.
    /// </summary>
    public ClientBuilder ConnectTimeout(TimeSpan timeout)
    {
        ValidateTimeout(timeout);
        return Configure(nameof(ConnectTimeout), h => h.ConnectTimeout = timeout);
    }

    /// <summary>
    /// How long an idle pooled connection is kept for reuse. The platform default is 1 minute.
    /// </summary>
    public ClientBuilder PoolIdleTimeout(TimeSpan timeout)
    {
        ValidateTimeout(timeout);
        return Configure(nameof(PoolIdleTimeout), h => h.PooledConnectionIdleTimeout = timeout);
    }

    /// <summary>
    /// How long a pooled connection may live before it is replaced, so long-running services pick up DNS changes.
    /// The platform default is unlimited.
    /// </summary>
    public ClientBuilder PoolConnectionLifetime(TimeSpan lifetime)
    {
        ValidateTimeout(lifetime);
        return Configure(nameof(PoolConnectionLifetime), h => h.PooledConnectionLifetime = lifetime);
    }

    /// <summary>
    /// The maximum number of simultaneous connections to one host (HTTP/1.1). The platform default is unlimited.
    /// </summary>
    public ClientBuilder MaxConnectionsPerHost(int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        return Configure(nameof(MaxConnectionsPerHost), h => h.MaxConnectionsPerServer = max);
    }

    /// <summary>
    /// Sets the HTTP version requests ask for. Requests can override it with <see cref="RequestBuilder.Version"/>.
    /// </summary>
    /// <param name="version">e.g. <see cref="System.Net.HttpVersion.Version20"/>.</param>
    /// <param name="policy">
    /// <see cref="HttpVersionPolicy.RequestVersionOrLower"/> (the default) allows falling back,
    /// <see cref="HttpVersionPolicy.RequestVersionOrHigher"/> allows upgrading,
    /// <see cref="HttpVersionPolicy.RequestVersionExact"/> fails if the version is unavailable.
    /// </param>
    public ClientBuilder HttpVersion(Version version, HttpVersionPolicy policy = HttpVersionPolicy.RequestVersionOrLower)
    {
        ArgumentNullException.ThrowIfNull(version);
        _requestDefaults = _requestDefaults with { Version = version, VersionPolicy = policy };
        return this;
    }

    /// <summary>
    /// Caches GET responses in <paramref name="cache"/> following RFC 9111 as a private cache: <c>max-age</c>,
    /// <c>Expires</c>, <c>Age</c>, <c>no-store</c>, <c>no-cache</c>, <c>must-revalidate</c>, <c>stale-while-revalidate</c>
    /// and <c>Vary</c>. Stale entries are revalidated with <c>If-None-Match</c> / <c>If-Modified-Since</c>, and a
    /// <c>304</c> serves the cached body. Successful unsafe requests (POST, PUT, ...) invalidate the URL's entry.
    /// </summary>
    /// <param name="cache">Storage, e.g. <c>new MemoryHttpCache()</c>.</param>
    /// <param name="clock">The clock for freshness; defaults to the system clock. Useful in tests.</param>
    public ClientBuilder Cache(IHttpCache cache, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _cacheClock = clock ?? TimeProvider.System;
        return this;
    }

    /// <summary>
    /// Waits for a permit from <paramref name="limiter"/> before every attempt (retries included), so the client
    /// stays within an API's published limits instead of reacting to <c>429</c>s. When the limiter refuses
    /// (queue full), <c>Send()</c> throws <see cref="ErrorKind.RateLimited"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// .RateLimit(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    /// {
    ///     TokenLimit = 10, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    ///     QueueLimit = 100, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    /// }))
    /// </code>
    /// </example>
    public ClientBuilder RateLimit(System.Threading.RateLimiting.RateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        _rateLimiter = limiter;
        return this;
    }

    /// <summary>
    /// Sets how <c>Query(object)</c> and <c>Form(object)</c> encode arrays and nested objects,
    /// e.g. <c>.QueryStyle(ArrayStyle.Brackets, NestedStyle.Brackets)</c> for Rails or PHP APIs.
    /// </summary>
    public ClientBuilder QueryStyle(ArrayStyle arrays, NestedStyle nested = NestedStyle.Reject)
    {
        _requestDefaults = _requestDefaults with { QueryStyle = new Nexar.QueryStyle(arrays, nested) };
        return this;
    }

    /// <summary>
    /// Fails reading a response body with <see cref="ErrorKind.Body"/> if it is larger than <paramref name="maxBytes"/>,
    /// so a misbehaving server cannot exhaust memory. No limit by default. Requests can override it with
    /// <see cref="RequestBuilder.MaxResponseSize"/>.
    /// </summary>
    public ClientBuilder MaxResponseSize(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _requestDefaults = _requestDefaults with { MaxResponseSize = maxBytes };
        return this;
    }

    /// <summary>
    /// Sends <c>Expect: 100-continue</c> with every request body, so servers can reject requests before large
    /// uploads. Requests can override it with <see cref="RequestBuilder.ExpectContinue"/>.
    /// </summary>
    public ClientBuilder ExpectContinue(bool expect = true)
    {
        _requestDefaults = _requestDefaults with { ExpectContinue = expect };
        return this;
    }

    /// <summary>
    /// How long to wait for the server's <c>100 Continue</c> before sending the body anyway. The platform default is 1 second.
    /// </summary>
    public ClientBuilder ExpectContinueTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        return Configure(nameof(ExpectContinueTimeout), h => h.Expect100ContinueTimeout = timeout);
    }

    /// <summary>
    /// Opens additional HTTP/2 connections to a server when the streams of one connection are exhausted,
    /// for high-throughput services.
    /// </summary>
    public ClientBuilder Http2MultipleConnections(bool enable = true) =>
        Configure(nameof(Http2MultipleConnections), h => h.EnableMultipleHttp2Connections = enable);

    /// <summary>
    /// Sends HTTP/2 keep-alive pings every <paramref name="interval"/>, and closes the connection if a ping is not
    /// answered within <paramref name="timeout"/>, so idle connections are not silently dropped by middleboxes.
    /// </summary>
    public ClientBuilder Http2KeepAlive(TimeSpan interval, TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        return Configure(nameof(Http2KeepAlive), h =>
        {
            h.KeepAlivePingDelay = interval;
            h.KeepAlivePingTimeout = timeout;
            h.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;
        });
    }

    /// <summary>
    /// Opens additional HTTP/3 connections to a server when the streams of one connection are exhausted.
    /// </summary>
    public ClientBuilder Http3MultipleConnections(bool enable = true) =>
        Configure(nameof(Http3MultipleConnections), h => h.EnableMultipleHttp3Connections = enable);

    /// <summary>
    /// Connects to the Unix domain socket at <paramref name="path"/> instead of a TCP host, e.g. the Docker Engine
    /// API at <c>/var/run/docker.sock</c>. Use any host in the URLs, e.g. <c>BaseUrl("http://localhost")</c>.
    /// </summary>
    public ClientBuilder UnixSocket(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _connectTarget = new ConnectTarget.Unix(path);
        return Configure("Connect", h => h.ConnectCallback = ConnectAsync);
    }

    /// <summary>
    /// Connects to the local named pipe <paramref name="pipeName"/> instead of a TCP host, e.g. <c>docker_engine</c>
    /// on Windows. Use any host in the URLs, e.g. <c>BaseUrl("http://localhost")</c>.
    /// </summary>
    public ClientBuilder NamedPipe(string pipeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        _connectTarget = new ConnectTarget.Pipe(pipeName);
        return Configure("Connect", h => h.ConnectCallback = ConnectAsync);
    }

    /// <summary>
    /// Connects to <paramref name="addresses"/> whenever a URL names <paramref name="host"/>, skipping DNS.
    /// The <c>Host</c> header and TLS certificate checks still use <paramref name="host"/>.
    /// </summary>
    public ClientBuilder Resolve(string host, params System.Net.IPAddress[] addresses)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        if (addresses.Length == 0)
        {
            throw new ArgumentException("At least one address is required.", nameof(addresses));
        }
        _resolve[host] = addresses;
        return Configure("Connect", h => h.ConnectCallback = ConnectAsync);
    }

    /// <summary>
    /// Sends from the local <paramref name="address"/>, for servers with several network interfaces.
    /// </summary>
    public ClientBuilder LocalAddress(System.Net.IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _localAddress = address;
        return Configure("Connect", h => h.ConnectCallback = ConnectAsync);
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        switch (_connectTarget)
        {
            case ConnectTarget.Unix unix:
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(unix.Path), cancellationToken).ConfigureAwait(false);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            case ConnectTarget.Pipe pipe:
            {
                var stream = new System.IO.Pipes.NamedPipeClientStream(".", pipe.Name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                try
                {
                    await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }

        var endPoint = context.DnsEndPoint;
        var addresses = _resolve.TryGetValue(endPoint.Host, out var overridden)
            ? overridden
            : await System.Net.Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            if (_localAddress != null && _localAddress.AddressFamily != address.AddressFamily)
            {
                continue;
            }

            var socket = new System.Net.Sockets.Socket(address.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (_localAddress != null)
                {
                    socket.Bind(new System.Net.IPEndPoint(_localAddress, 0));
                }
                await socket.ConnectAsync(new System.Net.IPEndPoint(address, endPoint.Port), cancellationToken).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw lastError ?? new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.AddressFamilyNotSupported);
    }

    private abstract record ConnectTarget
    {
        public sealed record Unix(string Path) : ConnectTarget;

        public sealed record Pipe(string Name) : ConnectTarget;
    }

    private ClientBuilder Configure(string setting, Action<SocketsHttpHandler> apply)
    {
        _handlerSettings[setting] = apply;
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
    /// <param name="jitter">
    /// Waits a random time between zero and the computed backoff ("full jitter"), so many clients recovering from
    /// the same outage do not retry in lockstep. A <c>Retry-After</c> delay is used as given.
    /// </param>
    public ClientBuilder Retry(int maxRetries, TimeSpan? delay = null, bool exponentialBackoff = true, TimeSpan? maxDelay = null, bool jitter = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        var retryDelay = delay ?? TimeSpan.FromSeconds(1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero, nameof(delay));
        var retryMaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryMaxDelay, TimeSpan.Zero, nameof(maxDelay));
        _retry = _retry with
        {
            MaxRetries = maxRetries,
            Delay = retryDelay,
            ExponentialBackoff = exponentialBackoff,
            MaxDelay = retryMaxDelay,
            Jitter = jitter
        };
        return this;
    }

    /// <summary>
    /// Changes which failed attempts are retried. Return <c>true</c> to retry, <c>false</c> to never retry,
    /// or <c>null</c> to keep Nexar's decision (<see cref="RetryContext.RetriedByDefault"/>).
    /// Applies when <see cref="Retry"/> is configured. Non-idempotent requests are still only retried when they
    /// use <see cref="RequestBuilder.Retryable"/> or <see cref="RequestBuilder.IdempotencyKey"/>, or when the
    /// connection failed.
    /// </summary>
    /// <example>
    /// <code>
    /// .RetryWhen(ctx => ctx.Response?.StatusCode == HttpStatusCode.Conflict ? true : null)
    /// </code>
    /// </example>
    public ClientBuilder RetryWhen(Func<RetryContext, bool?> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        _retry = _retry with { Condition = condition };
        return this;
    }

    /// <summary>
    /// Calls <paramref name="callback"/> before every re-send (retries and the re-send after a <c>401</c>),
    /// e.g. to log or count them. Exceptions from the callback are logged and ignored.
    /// </summary>
    public ClientBuilder OnRetry(Action<RetryEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _retry = _retry with { Callbacks = [.. _retry.Callbacks, callback] };
        return this;
    }

    /// <summary>
    /// Sets which encodings the default handler asks for and decodes. Defaults to gzip, deflate and Brotli.
    /// </summary>
    public ClientBuilder Decompression(System.Net.DecompressionMethods methods) =>
        Configure(nameof(Decompression), h => h.AutomaticDecompression = methods);

    /// <summary>
    /// Disables TLS certificate validation. Only use this for local development.
    /// </summary>
    public ClientBuilder DangerAcceptInvalidCerts(bool accept = true)
    {
        _acceptInvalidCerts = accept;
        if (accept)
        {
            return Configure(nameof(DangerAcceptInvalidCerts), _ => { });
        }
        _handlerSettings.Remove(nameof(DangerAcceptInvalidCerts));
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
    public NexarClient Build() => new(BuildOptions());

    internal ClientOptions BuildOptions()
    {
        Uri? baseUrl = null;
        if (_baseUrl != null && !UrlBuilder.TryParseHttpUrl(_baseUrl, out baseUrl))
        {
            throw new NexarException(ErrorKind.Builder, $"Base URL '{_baseUrl}' is not an absolute http(s) URL.");
        }

        foreach (var (name, value) in _defaultHeaders)
        {
            try
            {
                HeaderValidator.Validate(name, value);
            }
            catch (ArgumentException ex)
            {
                throw new NexarException(ErrorKind.Builder, $"Invalid default header: {ex.Message}", innerException: ex);
            }
        }

        if (_connectTarget != null && (_resolve.Count > 0 || _localAddress != null))
        {
            throw new NexarException(ErrorKind.Builder,
                "UnixSocket() and NamedPipe() cannot be combined with Resolve() or LocalAddress(), which apply to TCP connections.");
        }

        var handlerSettings = string.Join(", ", _handlerSettings.Keys.Select(k => k == "Connect" ? "UnixSocket()/NamedPipe()/Resolve()/LocalAddress()" : $"{k}()"));

        if (_httpClient != null && (_primaryHandler != null || _handlers.Count > 0 || _handlerSettings.Count > 0))
        {
            throw new NexarException(
                ErrorKind.Builder,
                $"HttpClient() cannot be combined with HttpMessageHandler(), AddHandler() or handler settings ({handlerSettings}); configure that HttpClient directly.");
        }

        if (_primaryHandler != null && _handlerSettings.Count > 0)
        {
            throw new NexarException(
                ErrorKind.Builder,
                $"HttpMessageHandler() cannot be combined with handler settings ({handlerSettings}); configure your handler directly.");
        }

        var jsonOptions = new JsonSerializerOptions(_jsonOptions);
        // Reflection is only wired in when the app allows it; trimmed/AOT apps pass JsonTypeInfo instead.
        if (jsonOptions.TypeInfoResolver is null && JsonSerializer.IsReflectionEnabledByDefault)
        {
            jsonOptions.TypeInfoResolver = CreateReflectionResolver();
        }
        if (jsonOptions.TypeInfoResolver is not null)
        {
            jsonOptions.MakeReadOnly();
        }

        if (_parent != null && (_httpClient != null || _primaryHandler != null || _handlers.Count > 0 || _handlerSettings.Count > 0))
        {
            throw new NexarException(ErrorKind.Builder,
                $"A client made with With() shares its parent's connection pool, so it cannot change handler settings ({handlerSettings}), HttpMessageHandler(), AddHandler() or HttpClient().");
        }

        var ownsHttpClient = _parent == null && _httpClient == null;
        var httpClient = _parent?.HttpClient ?? _httpClient ?? new System.Net.Http.HttpClient(BuildHandlerChain())
        {
            // Timeouts are enforced per request so they can be told apart from cancellation.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        return new ClientOptions(
            httpClient,
            ownsHttpClient,
            baseUrl,
            new Dictionary<string, string>(_defaultHeaders, StringComparer.OrdinalIgnoreCase),
            _timeout,
            _retry,
            jsonOptions,
            _authenticator,
            _logger,
            new Redactor(_redactHeaders, _redactQueryParameters),
            // Only the default handler is known to follow redirects; a 3xx with Location then means the limit was hit.
            RedirectLimit: _parent != null
                ? _parent.RedirectLimit
                : _httpClient == null && _primaryHandler == null && _redirects.MaxRedirects > 0 ? _redirects.MaxRedirects : null,
            _requestDefaults,
            _rateLimiter,
            _cache,
            _cacheClock,
            _defaultQuery.ToList(),
            _serializers.ToList());
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
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = _redirects.MaxRedirects > 0,
            MaxAutomaticRedirections = Math.Max(_redirects.MaxRedirects, 1),
            // Stateless unless CookieStore() is used.
            UseCookies = false
        };
        foreach (var apply in _handlerSettings.Values)
        {
            apply(handler);
        }
        if (_acceptInvalidCerts)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is true, a feature switch trimmed apps turn off.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Only called when JsonSerializer.IsReflectionEnabledByDefault is true, a feature switch Native AOT apps turn off.")]
    private static System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver CreateReflectionResolver() =>
        new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();

    internal static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive or InfiniteTimeSpan.");
        }
        return timeout;
    }
}
