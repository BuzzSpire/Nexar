using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nexar.Testing;

/// <summary>
/// A fluent mock <see cref="HttpMessageHandler"/> for testing code that uses Nexar (or any <see cref="HttpClient"/>).
/// </summary>
/// <example>
/// <code>
/// var mock = new MockHttp();
/// mock.OnGet("/users/1").RespondJson(new { id = 1, name = "Ada" });
/// mock.OnPost("/users").WithJsonBody&lt;User&gt;(u => u.Name == "Ada").Respond(HttpStatusCode.Created);
///
/// using var client = mock.CreateClient();
/// // ... exercise the code under test ...
/// mock.VerifyAllCalled();
/// </code>
/// </example>
public sealed class MockHttp : HttpMessageHandler
{
    private readonly List<MockRoute> _routes = new();
    private readonly ConcurrentQueue<MockRequest> _requests = new();

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<MockRequest> Requests => _requests.ToArray();

    /// <summary>
    /// Adds a route for <paramref name="method"/> and <paramref name="path"/>. The path is compared with the
    /// request's path (no query); a trailing <c>*</c> matches any rest, e.g. <c>/users/*</c>.
    /// Routes are tried in the order they were added; the first match answers.
    /// </summary>
    public MockRoute On(HttpMethod method, string path)
    {
        var route = new MockRoute(method, path);
        lock (_routes)
        {
            _routes.Add(route);
        }
        return route;
    }

    /// <summary>Adds a GET route.</summary>
    public MockRoute OnGet(string path) => On(HttpMethod.Get, path);

    /// <summary>Adds a POST route.</summary>
    public MockRoute OnPost(string path) => On(HttpMethod.Post, path);

    /// <summary>Adds a PUT route.</summary>
    public MockRoute OnPut(string path) => On(HttpMethod.Put, path);

    /// <summary>Adds a PATCH route.</summary>
    public MockRoute OnPatch(string path) => On(HttpMethod.Patch, path);

    /// <summary>Adds a DELETE route.</summary>
    public MockRoute OnDelete(string path) => On(HttpMethod.Delete, path);

    /// <summary>Adds a route that matches any method and path, e.g. as a catch-all added last.</summary>
    public MockRoute OnAny() => On(null!, "*");

    /// <summary>
    /// Creates a <see cref="NexarClient"/> that sends to this mock.
    /// </summary>
    public NexarClient CreateClient(string baseUrl = "https://mock.test", Action<ClientBuilder>? configure = null)
    {
        var builder = NexarClient.Builder().BaseUrl(baseUrl).HttpMessageHandler(this);
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// Throws if a route was never called, or was called a different number of times than <see cref="MockRoute.Times"/> expects.
    /// </summary>
    /// <exception cref="MockHttpException">A route's expectation is not met.</exception>
    public void VerifyAllCalled()
    {
        List<string> problems;
        lock (_routes)
        {
            problems = _routes
                .Where(r => r.ExpectedCalls is { } expected ? r.Calls != expected : r.Calls == 0)
                .Select(r => r.ExpectedCalls is { } expected
                    ? $"{r.Describe()} was called {r.Calls} time(s), expected {expected}"
                    : $"{r.Describe()} was never called")
                .ToList();
        }
        if (problems.Count > 0)
        {
            throw new MockHttpException("Unmet expectations:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "  " + p)));
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = await MockRequest.FromAsync(request, cancellationToken).ConfigureAwait(false);
        _requests.Enqueue(recorded);

        MockRoute? route;
        lock (_routes)
        {
            route = _routes.FirstOrDefault(r => r.Matches(recorded));
        }
        if (route == null)
        {
            string routes;
            lock (_routes)
            {
                routes = _routes.Count == 0 ? "  (none)" : string.Join(Environment.NewLine, _routes.Select(r => "  " + r.Describe()));
            }
            throw new MockHttpException($"No route matched {recorded.Method} {recorded.Url}.{Environment.NewLine}Routes:{Environment.NewLine}{routes}");
        }

        var response = await route.AnswerAsync(recorded, cancellationToken).ConfigureAwait(false);
        response.RequestMessage ??= request;
        return response;
    }
}

/// <summary>
/// A route of a <see cref="MockHttp"/>: what it matches and how it answers.
/// </summary>
public sealed class MockRoute
{
    private readonly HttpMethod? _method;
    private readonly string _path;
    private readonly List<Func<MockRequest, bool>> _conditions = new();
    private readonly List<string> _conditionNames = new();
    private readonly List<Func<MockRequest, HttpResponseMessage>> _responses = new();
    private TimeSpan _delay;
    private int _calls;

    internal MockRoute(HttpMethod? method, string path)
    {
        _method = method;
        _path = path;
    }

    /// <summary>How many requests this route answered.</summary>
    public int Calls => Volatile.Read(ref _calls);

    internal int? ExpectedCalls { get; private set; }

    /// <summary>Only matches requests with this query parameter value.</summary>
    public MockRoute WithQuery(string name, string value) => Matching(r => r.Query(name) == value, $"?{name}={value}");

    /// <summary>Only matches requests with this header value (request or content header).</summary>
    public MockRoute WithHeader(string name, string value) => Matching(r => r.Header(name) == value, $"{name}: {value}");

    /// <summary>Only matches requests with exactly this body.</summary>
    public MockRoute WithBody(string body) => Matching(r => r.Body == body, $"body \"{body}\"");

    /// <summary>Only matches requests whose JSON body deserializes to <typeparamref name="T"/> and satisfies <paramref name="predicate"/>.</summary>
    public MockRoute WithJsonBody<T>(Func<T, bool> predicate, JsonSerializerOptions? options = null) =>
        Matching(r => r.TryJson<T>(options, out var value) && predicate(value!), $"JSON {typeof(T).Name} body");

    /// <summary>Only matches requests that satisfy <paramref name="predicate"/>.</summary>
    public MockRoute Matching(Func<MockRequest, bool> predicate, string? description = null)
    {
        _conditions.Add(predicate);
        _conditionNames.Add(description ?? "custom condition");
        return this;
    }

    /// <summary>
    /// Answers with <paramref name="status"/> and an optional text body. Calling a Respond method several times
    /// builds a sequence; once it is used up, the last answer repeats.
    /// </summary>
    public MockRoute Respond(HttpStatusCode status = HttpStatusCode.OK, string? body = null, string mediaType = "text/plain") =>
        RespondWith(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body ?? "", Encoding.UTF8, mediaType)
        });

    /// <summary>Answers with <paramref name="value"/> serialized as JSON (web defaults: camelCase).</summary>
    public MockRoute RespondJson<T>(T value, HttpStatusCode status = HttpStatusCode.OK, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(value, options ?? MockRequest.WebOptions);
        return Respond(status, json, "application/json");
    }

    /// <summary>Answers with a response built from the request.</summary>
    public MockRoute RespondWith(Func<MockRequest, HttpResponseMessage> respond)
    {
        _responses.Add(respond);
        return this;
    }

    /// <summary>Fails like a transport error, e.g. <c>new HttpRequestException(HttpRequestError.ConnectionError)</c>.</summary>
    public MockRoute Throws(Exception exception) => RespondWith(_ => throw exception);

    /// <summary>Waits before answering, honoring cancellation, so timeouts can be tested.</summary>
    public MockRoute Delay(TimeSpan delay)
    {
        _delay = delay;
        return this;
    }

    /// <summary>Expects exactly <paramref name="calls"/> calls when <see cref="MockHttp.VerifyAllCalled"/> runs.</summary>
    public MockRoute Times(int calls)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(calls);
        ExpectedCalls = calls;
        return this;
    }

    internal bool Matches(MockRequest request)
    {
        if (_method != null && _method != request.Method)
        {
            return false;
        }
        var path = request.Url.AbsolutePath;
        var pathMatches = _path == "*"
            || (_path.EndsWith('*') ? path.StartsWith(_path[..^1], StringComparison.Ordinal) : path == _path);
        return pathMatches && _conditions.All(c => c(request));
    }

    internal async Task<HttpResponseMessage> AnswerAsync(MockRequest request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        }
        if (_responses.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        }
        return _responses[Math.Min(call, _responses.Count) - 1](request);
    }

    internal string Describe()
    {
        var description = $"{_method?.Method ?? "ANY"} {_path}";
        return _conditionNames.Count == 0 ? description : $"{description} [{string.Join(", ", _conditionNames)}]";
    }
}

/// <summary>
/// A request received by <see cref="MockHttp"/>, captured before it is disposed.
/// </summary>
public sealed record MockRequest(
    HttpMethod Method,
    Uri Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body)
{
    internal static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A header value (request or content header), with multiple values joined by <c>", "</c>; null if missing.</summary>
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>A query parameter value, or null.</summary>
    public string? Query(string name) => System.Web.HttpUtility.ParseQueryString(Url.Query)[name];

    /// <summary>The body deserialized as JSON (web defaults unless <paramref name="options"/> are given).</summary>
    public T? Json<T>(JsonSerializerOptions? options = null) =>
        Body == null ? default : JsonSerializer.Deserialize<T>(Body, options ?? WebOptions);

    internal bool TryJson<T>(JsonSerializerOptions? options, out T? value)
    {
        try
        {
            value = Json<T>(options);
            return value != null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    internal static async Task<MockRequest> FromAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new MockRequest(request.Method, request.RequestUri!, headers, body);
    }
}

/// <summary>
/// Thrown by <see cref="MockHttp"/> for an unmatched request or an unmet expectation.
/// </summary>
public sealed class MockHttpException(string message) : Exception(message);
