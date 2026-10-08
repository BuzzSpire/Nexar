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
| `Decompression(methods)` | Encodings to ask for and decode. Default: gzip, deflate and Brotli. |
| `DefaultHeader(name, value)`, `DefaultHeaders(...)`, `UserAgent(...)` | Headers sent with every request. A request header with the same name wins. |
| `JsonOptions(options)`, `JsonOptions(o => ...)` | JSON settings. Default: `JsonSerializerDefaults.Web` (camelCase, case-insensitive). |
| `Retry(maxRetries, delay, exponentialBackoff)` | Retries transient failures. Off by default. |
| `Auth(authenticator)` | Authenticates every request. See [Authentication](#authentication). |
| `Credentials(ICredentials)` | NTLM / Negotiate (Kerberos) through the platform handler. |
| `AddHandler(DelegatingHandler)` | Adds middleware (logging, auth refresh, Polly, ...). Runs in the order added. |
| `HttpMessageHandler(handler)` | Replaces the primary handler (tests, proxies). |
| `HttpClient(httpClient)` | Uses an existing `HttpClient`, e.g. from `IHttpClientFactory`. Nexar never disposes it. |
| `DangerAcceptInvalidCerts()` | Skips TLS validation. Local development only. |

## Requests

Start with `Get`, `Post`, `Put`, `Patch`, `Delete`, `Head` or `Request(method, url)`, then chain:

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
| Auth | `Auth(authenticator)`, `NoAuth()`, `BearerAuth(token)`, `BasicAuth(user, password)` |
| Path | `Path(name, value)` fills `{name}` in the URL, escaped as a path segment: `client.Get("/users/{id}").Path("id", id)` |
| Query | `Query(key, value)`, `Query(object)` (anonymous object or dictionary; arrays become `ids=1&ids=2`) |
| Body | `Json(value)`, `Form(object)`, `Multipart(form)`, `Body(string \| byte[] \| Stream, contentType)` |
| Other | `Timeout(TimeSpan)`, `Retryable(bool)`, `Version(Version)` |

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
```

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

string text  = await res.Text();
Report data  = await res.Json<Report>();
byte[] bytes = await res.Bytes();
Stream body  = await res.Stream();   // not buffered, for large downloads
```

`Text()`, `Bytes()` and `Json<T>()` can also be chained straight onto `Send()`. The response is then disposed for you:

```csharp
var report = await client.Get("/report").Send().ErrorForStatus().Json<Report>();
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
    var problem = e.Json<ProblemDetails>();                    // null if the body is not valid JSON
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
