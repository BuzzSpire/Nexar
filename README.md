![Nexar](https://socialify.git.ci/BuzzSpire/Nexar/image?description=1&descriptionEditable=Nexar%20is%20a%20C%23%20class%20used%20for%20sending%20and%20receiving%20HTTP%20requests.&font=Jost&forks=1&issues=1&language=1&name=1&owner=1&pattern=Circuit%20Board&pulls=1&stargazers=1&theme=Auto)

# Nexar

Nexar is a small, fluent HTTP client for .NET, inspired by Rust's [reqwest](https://docs.rs/reqwest). Build a client once, then describe each request through method chaining and finish it with `Send()`.

```csharp
var user = await client.Get("/users/1")
    .BearerAuth(token)
    .Send()
    .ErrorForStatus()
    .Json<User>();
```

## Installation

```bash
dotnet add package BuzzSpire.Nexar
```

## The client

Create one `NexarClient` and reuse it. It is thread-safe and owns its connection pool.

```csharp
using Nexar;

var client = NexarClient.Builder()
    .BaseUrl("https://api.example.com")
    .Timeout(TimeSpan.FromSeconds(30))
    .DefaultHeader("Accept", "application/json")
    .UserAgent("my-app/1.0")
    .Build();

// or with defaults
var client = new NexarClient();
```

| `ClientBuilder` method | What it does |
|---|---|
| `BaseUrl(url)` | Relative request URLs are joined to it. Absolute URLs bypass it. |
| `Timeout(TimeSpan)` | Total time for one attempt: sending, receiving headers, and reading the body with `Text()`/`Bytes()`/`Json<T>()`. `Stream()` is not limited. Default 100 s. |
| `ConnectTimeout(t)`, `PoolIdleTimeout(t)`, `PoolConnectionLifetime(t)`, `MaxConnectionsPerHost(n)` | Fail fast on unreachable hosts (TCP + TLS handshake), and tune connection reuse, e.g. a lifetime so DNS changes are picked up. |
| `UnixSocket(path)`, `NamedPipe(name)` | Talk HTTP to local daemons such as the Docker Engine API. |
| `Resolve(host, addresses...)`, `LocalAddress(ip)` | Skip DNS for a host (TLS and `Host` still use the name), or send from a specific interface. |
| `HttpVersion(version, policy)` | Default HTTP version and fallback policy for all requests, e.g. HTTP/2 or HTTP/3 only. |
| `ExpectContinue()`, `ExpectContinueTimeout(t)` | Send `Expect: 100-continue` with bodies so servers can reject large uploads early. |
| `Http2MultipleConnections()`, `Http2KeepAlive(interval, timeout)`, `Http3MultipleConnections()` | HTTP/2 and HTTP/3 connection tuning for high-throughput services. |
| `Decompression(methods)` | Encodings to ask for and decode. Default: gzip, deflate and Brotli. |
| `Proxy(url, credentials?)`, `ProxyBypass(hosts...)`, `NoProxy()` | HTTP(S) or SOCKS proxy; by default the system proxy and `HTTP(S)_PROXY`/`NO_PROXY` are used. |
| `CookieStore(jar?)` | Keeps `Set-Cookie` cookies and sends them back. Without it the client is stateless. |
| `Redirects(policy)` | `RedirectPolicy.Default` (up to 10 hops), `Limited(n)` or `None` (3xx returned as-is). Exceeding the limit throws `ErrorKind.Redirect`; HTTPS to HTTP is never followed. |
| `DefaultHeader(name, value)`, `DefaultHeaders(...)`, `UserAgent(...)` | Headers sent with every request. A request header with the same name wins. |
| `JsonOptions(options)`, `JsonOptions(o => ...)` | JSON settings. Default: `JsonSerializerDefaults.Web` (camelCase, case-insensitive). |
| `Retry(maxRetries, delay, exponentialBackoff)` | Retries transient failures. Off by default. |
| `Auth(authenticator)` | Authenticates every request. See [Authentication](#authentication). |
| `Credentials(ICredentials)` | NTLM / Negotiate (Kerberos) through the platform handler. |
| `AddHandler(DelegatingHandler)` | Adds middleware (logging, auth refresh, Polly, ...). Runs in the order added. |
| `HttpMessageHandler(handler)` | Replaces the primary handler (tests, proxies). |
| `HttpClient(httpClient)` | Uses an existing `HttpClient`, e.g. from `IHttpClientFactory`. Nexar never disposes it. |
| `ClientCertificate(cert)`, `AddRootCertificate(ca)`, `MinTlsVersion(version)` | mTLS client certificates, extra trusted CAs (host names are still checked), TLS 1.2+ or 1.3 only. |
| `DangerAcceptInvalidCerts()` | Skips TLS validation. Local development only. |
| `Logger(ILogger)`, `RedactHeaders(...)`, `RedactQueryParameters(...)` | Logging and secret redaction. See [Observability](#observability). |

## Requests

Start with `Get`, `Post`, `Put`, `Patch`, `Delete`, `Head`, `Options`, `Trace`, `Query` (the safe QUERY method), or `Request(method, url)` for any other method (`client.Request("PROPFIND", url)`), then chain:

```csharp
var response = await client.Post("/orders")
    .Header("Idempotency-Key", key)
    .BearerAuth(token)
    .Query("notify", true)
    .Json(new { ProductId = 42, Quantity = 1 })
    .Timeout(TimeSpan.FromSeconds(5))
    .Send(cancellationToken);
```

| Area | Methods |
|---|---|
| Headers | `Header(name, value)` (replace), `HeaderAppend(name, value)` (add another value), `Headers(pairs)`. Names must be RFC 9110 tokens and values must not contain CR, LF or other control characters; otherwise `Send()` throws `ErrorKind.Builder`. This makes header injection impossible. |
| Negotiation | `Accept(mediaTypes...)`, `AcceptLanguage(languages...)` (validated, quality values included) |
| Conditional | `IfNoneMatch(etag)`, `IfMatch(etag)`, `IfModifiedSince(date)`, `IfUnmodifiedSince(date)`; `res.IsNotModified` for 304 |
| Range | `Range(from, to)`, `RangeSuffix(length)`, `IfRange(etag or date)`; `res.IsPartialContent`, `res.ContentRange` |
| Auth | `Auth(authenticator)`, `NoAuth()`, `BearerAuth(token)`, `BasicAuth(user, password)` |
| Path | `Path(name, value)` fills `{name}` in the URL, escaped as a path segment: `client.Get("/users/{id}").Path("id", id)` |
| Query | `Query(key, value)`, `Query(object)` (anonymous object or dictionary; arrays become `ids=1&ids=2`) |
| Body | `Json(value)`, `Form(object)`, `Multipart(form)`, `File(path)`, `Body(string \| byte[] \| Stream, contentType)`, `Body(string, Encoding, mediaType)` |
| Other | `Timeout(TimeSpan)`, `Retryable(bool)`, `Version(version, policy?)`, `ExpectContinue(bool)` |

### Bodies

```csharp
await client.Post("/users").Json(new { Name = "Ada" }).Send();

await client.Post("/login").Form(new { Username = "ada", Password = "secret" }).Send();

await client.Post("/upload")
    .Multipart(new MultipartForm()
        .Text("title", "Holiday")
        .File("photo", bytes, "beach.jpg", "image/jpeg"))
    .Send();

await client.Put("/files/report.csv").Body(File.OpenRead("report.csv"), "text/csv").Send();

await client.Post("/legacy").Body(xml, Encoding.GetEncoding("iso-8859-9"), "application/xml").Send();  // charset is set for you
```

### Files

```csharp
// Upload: the content type comes from the extension, and the file is reopened on retries.
await client.Put("/files/report.pdf").File("report.pdf").Send();
await client.Post("/upload").Multipart(new MultipartForm().File("doc", "report.pdf")).Send();

// Download: written to a temporary file and moved into place only when complete.
await client.Get("/files/report.pdf").Send().ErrorForStatus().SaveTo("report.pdf");

// Resumable download: after a failure, the next call continues from where it stopped.
long size = await client.Get("/big.iso").IfRange(etag).DownloadTo("big.iso", resume: true);
```

`DownloadTo` keeps the unfinished data in `{path}.partial` and asks for the rest with a `Range` request. If the server answers with the whole resource instead (no range support, or `If-Range` no longer matches), it starts over. If it answers with a range that does not continue the file, you get `ErrorKind.Body`.

## Authentication

Set an authenticator for the whole client, or for a single request:

```csharp
var client = NexarClient.Builder()
    .BaseUrl("https://api.example.com")
    .Auth(Auth.OAuth2ClientCredentials("https://login.example.com/oauth/token", clientId, clientSecret, scope: "api.read"))
    .Build();

await client.Get("/legacy").Auth(Auth.Digest("user", "pass")).Send();   // overrides the client's
await client.Get("/health").NoAuth().Send();                            // no credentials
```

| `Auth` factory | Scheme |
|---|---|
| `Auth.Bearer(token)` | `Authorization: Bearer ...` with a fixed token |
| `Auth.Bearer(ct => GetTokenAsync(ct))` | Bearer token fetched before every request |
| `Auth.Basic(user, password)` | HTTP Basic (RFC 7617) |
| `Auth.ApiKeyHeader("X-Api-Key", key)` | API key in a header |
| `Auth.ApiKeyQuery("api_key", key)` | API key in the query string |
| `Auth.Digest(user, password)` | HTTP Digest (RFC 7616): MD5, SHA-256, `-sess` |
| `Auth.OAuth2ClientCredentials(...)` | OAuth 2.0 client credentials, with token caching and refresh |
| `Auth.Custom((request, ct) => ...)` | Anything else, e.g. HMAC or AWS SigV4 request signing |

For Windows authentication, use the platform handler: `.Credentials(CredentialCache.DefaultCredentials)`.

**Rules**

- A request-level `Auth()`, `NoAuth()`, `BearerAuth()` or `BasicAuth()` replaces the client's authenticator. So does an explicit `Authorization` header set with `Header()`.
- When the server answers `401`, the authenticator gets one chance to fix it: Digest reads the challenge, OAuth2 fetches a new token. The request is then sent once more. This re-send does not use up a retry, and it is skipped for `Stream` bodies.
- If credentials cannot be obtained (for example the token endpoint is down or rejects the client), you get a `NexarException` with `Kind = ErrorKind.Auth`.

OAuth2 with all options:

```csharp
.Auth(Auth.OAuth2ClientCredentials(new OAuth2ClientCredentialsOptions
{
    TokenUrl = "https://login.example.com/oauth/token",
    ClientId = clientId,
    ClientSecret = clientSecret,
    Scope = "api.read api.write",
    AdditionalParameters = new Dictionary<string, string> { ["audience"] = "https://api.example.com" },
    SendCredentialsInBody = true,                      // client_secret_post instead of Basic
    RefreshBeforeExpiry = TimeSpan.FromMinutes(1),
}))
```

Your own scheme: implement `IAuthenticator`:

```csharp
public sealed class HmacAuth(byte[] key) : IAuthenticator
{
    public async ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        request.Headers.Add("X-Signature", Convert.ToHexString(HMACSHA256.HashData(key, body)));
    }
}
```

## Responses

`Send()` returns a `NexarResponse` as soon as the headers arrive. Read the body the way you need it:

```csharp
using var res = await client.Get("/report").Send();

Console.WriteLine(res.StatusCode);
Console.WriteLine(res.ETag);                 // typed headers: ContentType, ETag, LastModified,
Console.WriteLine(res.Location);             // Location (absolute), RetryAfter
Console.WriteLine(res.Header("X-RateLimit-Remaining"));   // any header, or null

string text  = await res.Text();              // charset from Content-Type, else BOM, else UTF-8
string old   = await res.Text(Encoding.Latin1); // fallback for bodies without a charset
Report data  = await res.Json<Report>();
byte[] bytes = await res.Bytes();
Stream body  = await res.Stream();   // not buffered, for large downloads
```

`Text()`, `Bytes()` and `Json<T>()` can also be chained straight onto `Send()`. The response is then disposed for you:

```csharp
var report = await client.Get("/report").Send().ErrorForStatus().Json<Report>();
```

`Stream()` chains too. The returned stream owns the response, so disposing it releases the connection:

```csharp
await using var body = await client.Get("/big.zip").Send().ErrorForStatus().Stream();
await body.CopyToAsync(file);
```

## Errors

Everything Nexar throws is a `NexarException` with a `Kind`:

| `ErrorKind` | When |
|---|---|
| `Builder` | The request could not be built: invalid URL, relative URL without a base URL, unserializable body. Raised by `Send()`. |
| `Connect` | The connection could not be established. |
| `Timeout` | No response headers within the timeout. |
| `Request` | The request failed while being sent. |
| `Status` | `ErrorForStatus()` saw a 4xx or 5xx status. `StatusCode` is set. |
| `Body` | The response body could not be read. |
| `Decode` | `Json<T>()` got an empty, `null` or invalid body. |
| `Auth` | Credentials could not be obtained or applied, e.g. the OAuth token endpoint failed. |
| `Redirect` | A redirect was not followed: more hops than the `RedirectPolicy` allows, or HTTPS to HTTP. |

A 4xx/5xx status is **not** an exception by itself. It is a normal response until you call `ErrorForStatus()`:

```csharp
try
{
    var user = await client.Get("/users/1").Send().ErrorForStatus().Json<User>();
}
catch (NexarException e) when (e.IsStatus && e.StatusCode == HttpStatusCode.NotFound)
{
    // ...
}
catch (NexarException e) when (e.IsTimeout)
{
    // ...
}
```

`ErrorForStatus()` keeps what the server said, so error details are not lost:

```csharp
catch (NexarException e) when (e.IsStatus)
{
    Console.WriteLine(e.ResponseBody);                         // first 64 KB of the error body
    Console.WriteLine(e.ResponseHeaders?["X-Request-Id"][0]);
    var custom = e.Json<MyApiError>();                         // null if the body is not valid JSON

    if (e.Problem is { } problem)                              // RFC 9457 application/problem+json
    {
        Console.WriteLine($"{problem.Title}: {problem.Detail} ({problem.Type})");
        var errors = problem.Extension<Dictionary<string, string[]>>("errors");
    }
}
```

Cancelling through your own `CancellationToken` throws the usual `OperationCanceledException`, so a timeout and a cancellation never look the same.

## Retries

```csharp
var client = NexarClient.Builder()
    .Retry(maxRetries: 3, delay: TimeSpan.FromMilliseconds(200))   // 200, 400, 800 ms
    .Build();
```

What is retried:

- **Which failures:** connection errors, timeouts, and `408`, `429`, `500`, `502`, `503`, `504` responses.
- **Which methods:** only idempotent ones (`GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT`, `DELETE`), so a `POST` that may already have been processed is never sent twice. The one exception is a failed connection: the request never reached the server, so it is retried for any method. To opt a `POST`/`PATCH` in, typically together with an idempotency key, use `.Retryable()`. `.Retryable(false)` opts a request out.
- **`Retry-After`:** if the server sends it (seconds or a date), it replaces the computed delay. If it asks for more than `maxDelay` (default 30 s), retrying stops and you get that response.
- **Delays:** every delay is capped at `maxDelay`.
- **Bodies:** requests with a `Stream` body are sent only once.

After the last retry you get the last response (or exception) as usual.

```csharp
await client.Post("/payments")
    .Header("Idempotency-Key", key)
    .Json(payment)
    .Retryable()
    .Send();
```

## Observability

Nexar publishes an `ActivitySource` and a `Meter`, both named `Nexar`, following the OpenTelemetry HTTP client semantic conventions:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Nexar"))
    .WithMetrics(m => m.AddMeter("Nexar"));
```

- **Spans:** one per request, covering retries and re-authentication. Attributes: `http.request.method`, `url.full` (redacted), `server.address`, `server.port`, `http.response.status_code`, `http.request.resend_count` and `error.type`. Each re-send is a `nexar.resend` event with its reason (`503`, `timeout`, `connect`, `unauthorized`, ...). 4xx/5xx responses and failures mark the span as an error.
- **Metrics:** `http.client.request.duration` (s), `http.client.active_requests`, and `nexar.client.resends` tagged by reason. Metrics never carry the URL.
- **Logs:** with `.Logger(logger)`, the request line, status and duration go out at `Information`, re-sends at `Debug`, failures at `Warning`, and request/response headers at `Trace`.

Secrets are redacted everywhere: the values of `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie` and API key headers set through `Auth`, and query parameters such as `api_key`, `access_token`, `token` and `client_secret`. You can add more with `.RedactHeaders(...)` and `.RedactQueryParameters(...)`.

## Testing

Plug in your own handler; no mocking library needed:

```csharp
var client = NexarClient.Builder()
    .BaseUrl("https://api.test")
    .HttpMessageHandler(new MyFakeHandler())
    .Build();
```

## Migrating from 2.x

Version 3 replaces the whole API.

| 2.x | 3.x |
|---|---|
| `Nexar.Get<User>(url)` | `new NexarClient().Get(url).Send().Json<User>()` |
| `Nexar.Create(new NexarConfig { BaseUrl = ..., TimeoutMs = ... })` | `NexarClient.Builder().BaseUrl(...).Timeout(...).Build()` |
| `api.GetAsync<User>(url, headers)` | `client.Get(url).Headers(headers).Send().Json<User>()` |
| `api.PostAsync<User>(url, body)` | `client.Post(url).Json(body).Send().Json<User>()` |
| `ContentType.FormUrlEncoded` / `FormData` / `Binary` | `.Form(...)` / `.Multipart(...)` / `.Body(...)` |
| `.Request().Url(url).WithHeader(...).WithQuery(...)` | `client.Get(url).Header(...).Query(...)` |
| `AuthHelper.Bearer(token)` / `Basic(...)` / `ApiKey(...)` | `.BearerAuth(token)`, `.BasicAuth(...)`, `.Auth(Auth.ApiKeyHeader(...))` |
| `response.IsSuccess`, `response.Data` | `res.IsSuccess`, `await res.Json<T>()` |
| `response.ErrorMessage` / fake `Status = 500` on failure | `NexarException` with `Kind` |
| `NexarConfig.MaxRetryAttempts` | `.Retry(maxRetries)` |
| `IInterceptor` | `DelegatingHandler` via `.AddHandler(...)` |
| `NexarConfig.ValidateSslCertificates = false` | `.DangerAcceptInvalidCerts()` |

JSON now defaults to `JsonSerializerDefaults.Web` (camelCase output). To keep PascalCase, use `.JsonOptions(o => o.PropertyNamingPolicy = null)`.

## License

MIT
