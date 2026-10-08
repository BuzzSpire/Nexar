![Nexar](https://socialify.git.ci/BuzzSpire/Nexar/image?description=1&descriptionEditable=A%20small%2C%20fluent%20HTTP%20client%20for%20.NET&font=Jost&forks=1&issues=1&language=1&name=1&owner=1&pattern=Circuit%20Board&pulls=1&stargazers=1&theme=Auto)

# Nexar

[![NuGet](https://img.shields.io/nuget/v/BuzzSpire.Nexar.svg)](https://www.nuget.org/packages/BuzzSpire.Nexar)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Nexar is a small, fluent HTTP client for .NET, inspired by Rust's [reqwest](https://docs.rs/reqwest). You build one client, describe each request with method chaining, and read the response the way you need it.

```csharp
var user = await client.Get("/users/{id}").Path("id", 1).Send().ErrorForStatus().Json<User>();
```

Every code sample in this README is taken from [`samples/Nexar.Examples`](samples/Nexar.Examples), which runs them against a local demo API. A test fails if this README and the examples drift apart.

- **One way to do things.** `NexarClient`, then `RequestBuilder`, then `NexarResponse`, and one exception type, `NexarException`.
- **Safe defaults.** Retries only for idempotent requests, timeouts that cover the body, validated headers, redacted secrets in logs and traces.
- **Full HTTP.** Every method; JSON, form, multipart, file and stream bodies; conditional and range requests; redirects, proxies, cookies, mTLS, HTTP/2 and HTTP/3, Unix sockets.
- **Streaming.** Server-Sent Events, NDJSON, JSON arrays read one element at a time, `Link` pagination, progress reporting.
- **Production features.** Authentication (Bearer, Basic, API key, Digest, OAuth 2.0), an RFC 9111 cache, rate limiting, OpenTelemetry tracing and metrics, `ILogger`.
- **Ecosystem.** Native AOT, dependency injection with `IHttpClientFactory`, and a mock handler for tests.

## Contents

- [Installation](#installation)
- [Quick start](#quick-start)
- [How it fits together](#how-it-fits-together)
- [The client](#the-client)
- [Requests](#requests)
- [Responses](#responses)
- [Errors](#errors)
- [Retries and timeouts](#retries-and-timeouts)
- [Authentication](#authentication)
- [Streaming and pagination](#streaming-and-pagination)
- [Files](#files)
- [Caching](#caching)
- [Connections](#connections)
- [Observability](#observability)
- [Dependency injection](#dependency-injection)
- [Testing](#testing)
- [Native AOT and trimming](#native-aot-and-trimming)
- [Running the examples](#running-the-examples)
- [Migrating from 2026.3001.1 and earlier](#migrating-from-202630011-and-earlier)
- [Contributing](#contributing)

## Installation

```bash
dotnet add package BuzzSpire.Nexar
dotnet add package BuzzSpire.Nexar.Extensions.DependencyInjection   # optional: AddNexarClient()
dotnet add package BuzzSpire.Nexar.Testing                          # optional: MockHttp for tests
```

Nexar targets .NET 10 (LTS), and everything is in the `Nexar` namespace.

## Quick start

`baseUrl` is your API's address, for example `https://api.example.com`.

<!-- snippet: quick-start -->
```csharp
using var client = NexarClient.Builder()
    .BaseUrl(baseUrl)
    .Timeout(TimeSpan.FromSeconds(30))
    .UserAgent("my-app/1.0")
    .Build();

var user = await client.Get("/users/{id}")
    .Path("id", 1)
    .Send()
    .ErrorForStatus()
    .Json<User>();

Console.WriteLine(user.Name);   // Ada Lovelace
```

## How it fits together

| Type | Role |
|---|---|
| `NexarClient` | Reusable and thread-safe; owns the connection pool. Create one and share it. |
| `ClientBuilder` | Client-wide settings: base URL, timeouts, headers, retries, auth, cache, TLS, proxy, ... |
| `RequestBuilder` | One request, configured by chaining, finished with `Send()` (or `Build()`). |
| `NexarResponse` | Status and headers right away; the body is read when you ask: `Text()`, `Json<T>()`, `Bytes()`, `Stream()`, ... |
| `NexarException` | The only exception Nexar throws. `Kind` says what went wrong. |

`Send()` returns `Task<NexarResponse>`, and the readers can be chained onto it: `Send().ErrorForStatus().Json<T>()`. When a chain ends in a reader, the response is disposed for you.

## The client

```csharp
var client = NexarClient.Builder()
    .BaseUrl("https://api.example.com/v1")
    .Timeout(TimeSpan.FromSeconds(30))
    .DefaultHeader("Accept", "application/json")
    .Retry(3)
    .Build();

var client = new NexarClient();   // all defaults
```

Need slightly different defaults, such as another base path, tenant headers or other credentials? `With()` derives a client that shares the same connection pool:

```csharp
using var tenant = client.With(b => b
    .BaseUrl("https://api.example.com/v1/tenants/acme")
    .DefaultHeader("X-Tenant", "acme")
    .Auth(Auth.Bearer(acmeToken)));
```

Disposing a derived client does not close the pool. Handler settings such as proxies, TLS and cookies belong to the parent.

| `ClientBuilder` | What it does |
|---|---|
| `BaseUrl(url)` | Relative request URLs are joined to it. Absolute URLs bypass it. |
| `Timeout(t)` | Total time for one attempt: connecting, sending, receiving headers, and reading the body with `Text`/`Bytes`/`Json`. `Stream()` and the streaming readers are not limited. Default 100 s. |
| `DefaultHeader(name, value)`, `DefaultHeaders(...)`, `UserAgent(...)`, `DefaultQuery(name, value)` | Sent with every request (e.g. `?api-version=...`). A request header or query parameter with the same name replaces it. |
| `JsonOptions(options)`, `JsonOptions(o => ...)` | Default: `JsonSerializerDefaults.Web` (camelCase, case-insensitive). |
| `Serializer(serializer)` | Registers a body format such as XML or MessagePack for `Serialized()` and `As<T>()`. |
| `QueryStyle(arrays, nested)` | How `Query(object)` and `Form(object)` encode arrays and nested objects. |
| `Retry(maxRetries, delay, exponentialBackoff, maxDelay, jitter)`, `RetryWhen(ctx => ...)`, `OnRetry(e => ...)` | See [Retries and timeouts](#retries-and-timeouts). Off by default. |
| `Auth(authenticator)`, `Credentials(ICredentials)` | See [Authentication](#authentication). |
| `Cache(cache)` | See [Caching](#caching). |
| `RateLimit(limiter)` | Waits for a `System.Threading.RateLimiting` permit before every attempt. A refused permit throws `ErrorKind.RateLimited`. |
| `MaxResponseSize(bytes)` | Fail with `ErrorKind.Body` instead of buffering huge bodies. No limit by default. |
| `HttpVersion(version, policy)`, `ExpectContinue()` | Defaults for every request. |
| `Logger(ILogger)`, `RedactHeaders(...)`, `RedactQueryParameters(...)` | See [Observability](#observability). |
| `OnRequest((request, ct) => ...)`, `OnResponse((response, ct) => ...)` | Lightweight hooks run for every attempt (after authentication, and when headers arrive), e.g. computed headers or metrics. |
| `AddHandler(DelegatingHandler)` | Middleware, run in the order added. |
| `HttpMessageHandler(handler)`, `HttpClient(httpClient)` | Bring your own handler (tests) or `HttpClient` (`IHttpClientFactory`). Nexar never disposes a client you pass in. |
| Connection settings | `Redirects`, `Proxy`, `CookieStore`, `ClientCertificate`, `ConnectTimeout`, `UnixSocket`, ... See [Connections](#connections). |

## Requests

Start with `Get`, `Post`, `Put`, `Patch`, `Delete`, `Head`, `Options`, `Trace`, `Query` (the safe QUERY method), or `Request(method, url)` for any other method, such as `client.Request("PROPFIND", url)`.

<!-- snippet: requests-query-headers -->
```csharp
var matches = await client.Get("/users")
    .Query("name", "ada")
    .Header("X-Request-Id", Guid.NewGuid().ToString())
    .Accept("application/json")
    .Send()
    .ErrorForStatus()
    .Json<List<User>>();
```

| Area | Methods |
|---|---|
| URL | `Path(name, value)` fills `{name}`, escaped as a path segment. `Query(key, value)`, `Query(pairs)`, `Query(object)`, `Query(object, QueryStyle)` |
| Headers | `Header(name, value)` (replace), `HeaderAppend(name, value)` (add), `Headers(pairs)`. Names and values are validated, so header injection is impossible. |
| Negotiation | `Accept(types...)`, `AcceptLanguage(languages...)` |
| Conditional | `IfNoneMatch(etag)`, `IfMatch(etag)`, `IfModifiedSince(date)`, `IfUnmodifiedSince(date)` |
| Range | `Range(from, to)`, `RangeSuffix(length)`, `IfRange(etag or date)` |
| Auth | `Auth(authenticator)`, `NoAuth()`, `BearerAuth(token)`, `BasicAuth(user, password)` |
| Body | `Json(value)`, `JsonStreamed(value)`, `JsonLines(asyncItems)`, `Form(...)`, `Multipart(form)`, `File(path)`, `Body(string \| byte[] \| ReadOnlyMemory<byte> \| Stream \| HttpContent)`, `Body(() => content)`, `Body(text, Encoding, mediaType)` |
| Behavior | `Timeout(t)`, `Retryable(bool)`, `IdempotencyKey()`, `Version(version, policy)`, `ExpectContinue()`, `Compress(ContentEncoding)`, `MaxResponseSize(bytes)`, `NoCache()`, `OnlyIfCached()` |
| Progress | `UploadProgress(progress)`, `DownloadProgress(progress)` |
| Send | `Send(ct)`, `Build()` then `client.Execute(request)`, `TryClone()`, `DownloadTo(path, resume)`, `Paginate<T>()` |

Mistakes made while building a request, such as a missing path parameter, an invalid header or an unserializable body, are raised by `Send()` as `ErrorKind.Builder`, so a chain is never interrupted halfway.

### Bodies

<!-- snippet: requests-json-body -->
```csharp
using var created = await client.Post("/users")
    .Json(new NewUser("Grace Hopper", "grace@example.com"))
    .Send()
    .ErrorForStatus();

Console.WriteLine(created.StatusCode);   // Created
Console.WriteLine(created.Location);     // http://.../users/3
var grace = await created.Json<User>();
```

<!-- snippet: requests-form-body -->
```csharp
var login = await client.Post("/login")
    .Form(new Dictionary<string, string> { ["username"] = "ada", ["password"] = "secret" })
    .Send()
    .ErrorForStatus()
    .Text();
```

<!-- snippet: requests-multipart -->
```csharp
var upload = await client.Post("/upload")
    .Multipart(new MultipartForm()
        .Text("title", "Quarterly report")
        .File("file", Encoding.UTF8.GetBytes("a,b\n1,2"), "report.csv", "text/csv"))
    .Send()
    .ErrorForStatus()
    .Text();
```

- `File(path)` sends a file, with the content type taken from its extension. The file is reopened for every attempt, so the request can be retried.
- `Body(text, contentType)` encodes the text with the charset named in `contentType`.
- `Compress(ContentEncoding.Gzip)` compresses the body on the fly.
- `JsonLines(asyncItems)` streams an `IAsyncEnumerable<T>` as NDJSON while it is produced, and `JsonStreamed(value)` serializes a huge object straight to the network. Neither is buffered.
- Stream bodies are sent once and never retried.

### XML and other formats

Besides JSON, bodies can be written and read with any `IContentSerializer`. XML (`XmlContentSerializer`) ships in the box:

```csharp
await client.Post("/invoices").Body(invoice, XmlContentSerializer.Default).Send();
var invoice = await client.Get("/invoices/7").Send().As<Invoice>();   // picked by Content-Type
```

Register your own format (Newtonsoft.Json, MessagePack, Protobuf, ...) with `.Serializer(mySerializer)`. `Serialized(value)` then writes with it, and `As<T>()` reads every content type it supports.

### Query and form styles

`Query(object)` and `Form(object)` flatten objects with your JSON naming policy:

| Style | Result |
|---|---|
| `ArrayStyle.Repeat` (default) | `ids=1&ids=2` |
| `ArrayStyle.Brackets` | `ids[]=1&ids[]=2` |
| `ArrayStyle.Comma` | `ids=1,2` |
| `ArrayStyle.Index` | `ids[0]=1&ids[1]=2` |
| `NestedStyle.Reject` (default) | nested objects are an error |
| `NestedStyle.Brackets` | `filter[status]=open` |
| `NestedStyle.Dot` | `filter.status=open` |

### Build now, send later

`Build()` gives you the finished request to inspect or sign before it is sent:

<!-- snippet: build-and-sign -->
```csharp
var request = client.Post("/users").Json(new NewUser("Edsger Dijkstra", "edsger@example.com")).Build();

var body = await request.ReadBodyAsync();   // the exact bytes that will be sent
var signature = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(key, body!));
request.SetHeader("X-Signature", signature);

using var response = await client.Execute(request);
```

## Responses

<!-- snippet: responses -->
```csharp
using var response = await client.Get("/users/1").Send();

Console.WriteLine(response.StatusCode);              // OK
Console.WriteLine(response.IsSuccess);               // True
Console.WriteLine(response.ContentType?.MediaType);  // application/json
Console.WriteLine(response.Header("Date"));           // any header, or null

string text = await response.Text();        // the body is read on demand...
User user = await response.Json<User>();    // ...and buffered, so it can be read again
byte[] bytes = await response.Bytes();
```

| Member | |
|---|---|
| `StatusCode`, `Status`, `IsSuccess`, `IsNotModified`, `IsPartialContent`, `ReasonPhrase`, `Url`, `Version` | Status line and final URL (after redirects) |
| `Headers`, `ContentHeaders`, `Header(name)`, `Trailers` | Raw headers |
| `ContentType`, `ETag`, `LastModified`, `Location`, `RetryAfter`, `ContentRange`, `ContentLength`, `Links` | Typed headers; `Location` is absolute, `Links` comes from the RFC 8288 `Link` header |
| `CacheStatus` | `Miss`, `Hit`, `Revalidated`, `Stale` or `None` |
| `Text()`, `Text(fallbackEncoding)`, `Bytes()`, `Json<T>()`, `As<T>()`, `As<T>(serializer)`, `Stream()` | Read the body. Text uses the charset from `Content-Type`, else a BOM, else UTF-8. `As<T>()` picks the serializer for the content type (JSON, XML or a registered one). |
| `SaveTo(path)` | Atomic download to a file |
| `Events()`, `JsonLines<T>()`, `JsonStream<T>()` | Streaming readers |
| `ErrorForStatus()` | Throws for 4xx/5xx and returns the response otherwise |

For legacy code pages such as `windows-1254` or `Shift_JIS`, call `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` once at startup.

## Errors

A 4xx/5xx response is a normal response until you call `ErrorForStatus()`. Everything else that goes wrong is a `NexarException`:

<!-- snippet: errors -->
```csharp
try
{
    await client.Get("/users/42").Send().ErrorForStatus();
}
catch (NexarException e) when (e.IsStatus)
{
    Console.WriteLine(e.StatusCode);       // NotFound
    Console.WriteLine(e.Problem?.Title);   // User not found (RFC 9457 problem details)
    Console.WriteLine(e.ResponseBody);     // the raw error body
}

try
{
    await client.Post("/users").Json(new NewUser("", "nobody@example.com")).Send().ErrorForStatus();
}
catch (NexarException e) when (e.Problem is { } problem)
{
    var errors = problem.Extension<Dictionary<string, string[]>>("errors");
    Console.WriteLine(errors?["Name"][0]);   // The Name field is required.
}

try
{
    await client.Get("/slow").Timeout(TimeSpan.FromMilliseconds(200)).Send();
}
catch (NexarException e) when (e.IsTimeout)
{
    Console.WriteLine(e.Kind);   // Timeout
}
```

| `ErrorKind` | When |
|---|---|
| `Builder` | The request could not be built: invalid URL or header, missing path parameter, unserializable body. |
| `Connect` | No connection: DNS failure, refused connection, TLS failure. |
| `Timeout` | The request or reading its body took longer than the timeout (or than `ConnectTimeout`). |
| `Request` | The request failed while being sent. |
| `Status` | `ErrorForStatus()` saw a 4xx/5xx. `StatusCode`, `ResponseBody` (first 64 KB), `ResponseHeaders`, `Problem` and `Json<T>()` describe it. |
| `Body` | The body could not be read, or is larger than `MaxResponseSize`. |
| `Decode` | `Json<T>()` got an empty, `null` or invalid body, or an unknown charset. |
| `Auth` | Credentials could not be obtained, for example the OAuth token endpoint failed. |
| `Redirect` | Too many redirects, or a redirect from HTTPS to HTTP. |
| `RateLimited` | The client-side rate limiter refused the request. `RetryAfter` says how long to wait. |

Cancelling through your own `CancellationToken` throws the usual `OperationCanceledException`, so a timeout and a cancellation never look the same.

## Retries and timeouts

<!-- snippet: retries -->
```csharp
using var client = NexarClient.Builder()
    .BaseUrl(baseUrl)
    .Retry(maxRetries: 3, delay: TimeSpan.FromMilliseconds(100))   // 100, 200, 400 ms
    .Build();

// /flaky answers 503 twice, then 200
var text = await client.Get("/flaky").Send().ErrorForStatus().Text();
```

- **What is retried:** connection errors, timeouts, and `408`, `429`, `500`, `502`, `503` and `504` responses.
- **Which methods:** only idempotent ones (`GET`, `HEAD`, `OPTIONS`, `TRACE`, `PUT`, `DELETE`, `QUERY`), so a `POST` is never sent twice. The exception is a failed connection, which never reached the server. Opt a request in with `.Retryable()`, typically with an idempotency key, or out with `.Retryable(false)`.
- **`Retry-After`:** replaces the computed delay. If it asks for more than `maxDelay` (default 30 s), you get that response back.
- **Streams:** stream bodies are never re-sent.
- **Jitter:** `Retry(..., jitter: true)` waits a random time up to the computed backoff, so clients recovering from the same outage do not retry in lockstep.
- **Custom conditions:** `RetryWhen(ctx => ...)` returns `true` to retry, `false` to veto, or `null` to keep the default. `ctx` carries the attempt, the response or the exception.
- **Idempotency keys:** `.IdempotencyKey()` sets one `Idempotency-Key` for all attempts and makes a `POST` retryable.
- **Callbacks:** `OnRetry(e => ...)` is called before every re-send, with the reason and the delay.
- **Timeouts:** `Timeout()` on the client or the request covers the whole attempt, body included. `ConnectTimeout()` limits connecting separately.

## Authentication

<!-- snippet: auth-bearer -->
```csharp
using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

var login = await client.Post("/login")
    .Form(new Dictionary<string, string> { ["username"] = "ada", ["password"] = "secret" })
    .Send()
    .ErrorForStatus()
    .Json<Dictionary<string, string>>();

var me = await client.Get("/me").BearerAuth(login["token"]).Send().ErrorForStatus().Json<User>();
```

<!-- snippet: auth-oauth2 -->
```csharp
using var reports = NexarClient.Builder()
    .BaseUrl(baseUrl)
    .Auth(Auth.OAuth2ClientCredentials($"{baseUrl}/oauth/token", "reports-app", "s3cret"))
    .Build();

// The token is fetched once, cached until shortly before it expires, and refreshed after a 401.
var daily = await reports.Get("/reports/daily").Send().ErrorForStatus().Text();
```

| `Auth` factory | Scheme |
|---|---|
| `Auth.Bearer(token)` | Fixed bearer token |
| `Auth.Bearer(ct => GetTokenAsync(ct))` | Token fetched before every request |
| `Auth.Bearer((forceRefresh, ct) => ...)` | Same, plus one forced refresh and re-send after a `401` |
| `Auth.Basic(user, password)` | HTTP Basic (RFC 7617) |
| `Auth.ApiKeyHeader(name, key)`, `Auth.ApiKeyQuery(name, key)` | API keys (always redacted from logs) |
| `Auth.Digest(user, password)` | HTTP Digest (RFC 7616): MD5, SHA-256, `-sess` |
| `Auth.OAuth2ClientCredentials(...)` | OAuth 2.0 client credentials with caching and refresh |
| `Auth.OAuth2RefreshToken(options)` | Keeps a user access token fresh with the refresh token grant, persisting rotated refresh tokens via `OnTokensRefreshed` |
| `Auth.Custom((request, ct) => ...)` | Any signing scheme (HMAC, AWS SigV4, ...) |
| `ClientBuilder.Credentials(...)` | NTLM / Negotiate (Kerberos) via the platform |

For apps that sign users in (desktop, mobile, CLI), `OAuth2` covers the authorization code flow with PKCE:

```csharp
var pkce = OAuth2.CreatePkce();   // send pkce.Challenge (S256) in the browser sign-in URL
var tokens = await OAuth2.ExchangeCodeAsync(null, tokenUrl, clientId, code, pkce.Verifier, redirectUri);

var api = NexarClient.Builder()
    .BaseUrl(apiUrl)
    .Auth(Auth.OAuth2RefreshToken(new OAuth2RefreshTokenOptions
    {
        TokenUrl = tokenUrl,
        ClientId = clientId,
        RefreshToken = tokens.RefreshToken!,
        InitialTokens = tokens,
        OnTokensRefreshed = (fresh, ct) => secureStore.SaveAsync(fresh.RefreshToken, ct)   // rotated tokens
    }))
    .Build();
```

A rejected refresh token (`invalid_grant`) raises `ErrorKind.Auth`, which means the user has to sign in again.

Set an authenticator on the client with `.Auth(...)` or on one request. A request-level `Auth()`, `NoAuth()`, `BearerAuth()` or an explicit `Authorization` header replaces the client's. After a `401`, the authenticator gets one chance to fix it (Digest challenge, token refresh). The request is then re-sent once, without using up a retry. You can write your own by implementing `IAuthenticator`.

## Streaming and pagination

<!-- snippet: streaming-sse -->
```csharp
await foreach (var e in client.Get("/events").Send().ErrorForStatus().Events())
{
    if (e.Event == "done")
    {
        break;
    }
    Console.Write(e.Data);   // Hello from Nexar
}
Console.WriteLine();
```

<!-- snippet: streaming-ndjson -->
```csharp
await foreach (var entry in client.Get("/logs").Send().JsonLines<LogEntry>())
{
    Console.WriteLine($"{entry.Level}: {entry.Message}");
}
```

<!-- snippet: pagination -->
```csharp
var names = new List<string>();
await foreach (var repo in client.Get("/repos").Paginate<Repo>())   // follows Link: <...>; rel="next"
{
    names.Add(repo.Name);
}
Console.WriteLine(names.Count);   // 6, from 3 pages
```

- The SSE parser follows the WHATWG rules: multi-line `data:`, comments, `id:` carrying over to later events, and `retry:`.
- `JsonStream<T>()` reads a huge JSON array one element at a time.
- `Paginate<TPage, TItem>(page => page.Items)` handles wrapped pages.
- None of these buffer the body, and the client timeout does not apply to them; use a cancellation token instead. The response is disposed when the loop ends, even on `break`.

## Files

<!-- snippet: files -->
```csharp
// Saved to a temporary file and moved into place only when complete.
await client.Get("/files/report.csv")
    .DownloadProgress(new Progress<TransferProgress>(p => Console.Write($"\r{p.BytesTransferred} bytes")))
    .Send()
    .ErrorForStatus()
    .SaveTo(path);
Console.WriteLine();

// Resumable: after a failure, the next call continues from where it stopped.
long size = await client.Get("/files/report.csv").DownloadTo(path, resume: true);

// Just the last 100 bytes
var tail = await client.Get("/files/report.csv").RangeSuffix(100).Send().Text();
```

`DownloadTo` keeps unfinished data in `{path}.partial` and asks for the rest with a `Range` request. If the server sends the whole resource instead, it starts over. Uploads work the same way: `client.Put(url).File("report.pdf")` and `new MultipartForm().File("doc", "report.pdf")`.

## Caching

<!-- snippet: caching -->
```csharp
using var client = NexarClient.Builder()
    .BaseUrl(baseUrl)
    .Cache(new MemoryHttpCache())
    .Build();

using var first = await client.Get("/catalog").Send();               // Cache-Control: max-age=60
using var second = await client.Get("/catalog").Send();              // served from memory
using var checkedAgain = await client.Get("/catalog").NoCache().Send(); // If-None-Match -> 304

Console.WriteLine($"{first.CacheStatus} {second.CacheStatus} {checkedAgain.CacheStatus}");
// Miss Hit Revalidated
```

The cache follows RFC 9111 as a private cache:

- **Freshness:** `max-age`, `Expires`, `Age` and `Date`.
- **Validation:** stale entries are revalidated with `If-None-Match` / `If-Modified-Since`, and a `304` serves the cached body with refreshed headers.
- **Directives:** `no-store`, `no-cache`, `must-revalidate` and `stale-while-revalidate` (the stale copy is served while a background refresh runs).
- **Variants:** `Vary` keeps one entry per variant.
- **Invalidation:** a successful POST/PUT/PATCH/DELETE invalidates the URL. Conditional or ranged requests skip the cache.
- **Memory:** large bodies (over `MaxEntryBytes`) stream through without being buffered.

`OnlyIfCached()` never touches the network and returns `504` if nothing fresh is cached. `MemoryHttpCache` is a size-bounded LRU; implement `IHttpCache` for a disk or distributed cache.

## Connections

| `ClientBuilder` | What it does |
|---|---|
| `Redirects(RedirectPolicy.Default \| Limited(n) \| None)` | Default: up to 10 hops; exceeding them throws `ErrorKind.Redirect`. `None` returns the 3xx response. HTTPS to HTTP is never followed. |
| `Proxy(url, credentials)`, `ProxyBypass(hosts...)`, `NoProxy()` | HTTP, HTTPS or SOCKS proxies. By default the system proxy and `HTTP(S)_PROXY`/`NO_PROXY` are used. |
| `CookieStore(jar?)` | Keeps cookies from `Set-Cookie`. Without it, the client is stateless. |
| `ClientCertificate(cert)`, `AddRootCertificate(ca)`, `MinTlsVersion(version)` | mTLS, private CAs (host names are still checked), TLS 1.2+ or 1.3 only |
| `PinCertificate(host, "sha256/...")`, `ClientBuilder.ComputePin(cert)` | Certificate pinning: the chain must contain a key matching a pin (checked after normal validation). Pin a backup key too. |
| `DangerAcceptInvalidCerts()` | Skips TLS validation; local development only |
| `ConnectTimeout(t)`, `PoolIdleTimeout(t)`, `PoolConnectionLifetime(t)`, `MaxConnectionsPerHost(n)` | Fail fast and tune connection reuse, e.g. a lifetime so DNS changes are picked up |
| `Decompression(methods)` | Default: gzip, deflate and Brotli |
| `HttpVersion(version, policy)`, `Http2MultipleConnections()`, `Http2KeepAlive(...)`, `Http3MultipleConnections()` | HTTP/2 and HTTP/3 |
| `ExpectContinue()`, `ExpectContinueTimeout(t)` | Lets servers reject large uploads before the body is sent |
| `UnixSocket(path)`, `NamedPipe(name)` | Local daemons such as the Docker Engine API |
| `Resolve(host, ips...)`, `LocalAddress(ip)` | Skip DNS for a host (TLS and `Host` still use the name), or pick the outgoing interface |

Connection settings configure Nexar's own handler, so they cannot be combined with `HttpMessageHandler()` or `HttpClient()`; `Build()` names the conflicting settings.

## Observability

Nexar publishes an `ActivitySource` and a `Meter`, both named `Nexar`, following the OpenTelemetry HTTP client semantic conventions:

<!-- snippet: observability -->
```csharp
var spans = new List<Activity>();
var metrics = new List<Metric>();
using var tracing = Sdk.CreateTracerProviderBuilder()
    .AddSource("Nexar")
    .AddInMemoryExporter(spans)      // or AddConsoleExporter() / AddOtlpExporter()
    .Build();
using var meters = Sdk.CreateMeterProviderBuilder()
    .AddMeter("Nexar")
    .AddInMemoryExporter(metrics)
    .Build();

using var client = NexarClient.Builder().BaseUrl(baseUrl).Retry(3, TimeSpan.FromMilliseconds(50)).Build();
await client.Get("/flaky").Query("api_key", "secret").Send();
meters.ForceFlush();

var span = spans.Single();
Console.WriteLine(span.GetTagItem("url.full"));                  // .../flaky?api_key=REDACTED
Console.WriteLine(span.GetTagItem("http.response.status_code")); // 200
Console.WriteLine(span.GetTagItem("http.request.resend_count")); // 2 (two 503s were retried)
Console.WriteLine(string.Join(", ", metrics.Select(m => m.Name)));
// http.client.request.duration, http.client.active_requests, nexar.client.resends
```

- **Spans:** one span per request, covering retries and re-authentication, with a `nexar.resend` event per re-send. 4xx/5xx and failures mark the span as an error.
- **Logs:** `.Logger(logger)` logs the request line, status and duration at `Information`, re-sends at `Debug`, failures at `Warning`, and headers at `Trace`.
- **Redaction:** secrets are redacted everywhere: `Authorization`, cookies, API key headers and parameters, and query parameters such as `api_key`, `access_token` and `client_secret`. Add more with `.RedactHeaders(...)` and `.RedactQueryParameters(...)`.

## Dependency injection

`BuzzSpire.Nexar.Extensions.DependencyInjection` builds clients on top of `IHttpClientFactory`:

<!-- snippet: dependency-injection -->
```csharp
var services = new ServiceCollection();
services.AddNexarClient<UsersApi>(b => b.BaseUrl(baseUrl).Retry(2));

await using var provider = services.BuildServiceProvider();
var user = await provider.GetRequiredService<UsersApi>().Get(2);
Console.WriteLine(user.Name);   // Alan Turing
```

```csharp
public sealed class UsersApi(NexarClient http)
{
    public Task<User> Get(int id) => http.Get("/users/{id}").Path("id", id).Send().ErrorForStatus().Json<User>();
}
```

`AddNexarClient(...)` returns the `IHttpClientBuilder`, so `AddHttpMessageHandler`, resilience handlers and logging still apply. Named clients come from `INexarClientFactory.CreateClient(name)`. Put handler-level settings on the `IHttpClientBuilder`, for example with `ConfigurePrimaryHttpMessageHandler`.

## Testing

`BuzzSpire.Nexar.Testing` provides `MockHttp`, a fluent mock handler:

<!-- snippet: testing -->
```csharp
var mock = new MockHttp();
mock.OnGet("/users/1").RespondJson(new User(1, "Mock User", "mock@example.com"));
mock.OnGet("/users/2")
    .Respond(HttpStatusCode.ServiceUnavailable)                         // first call
    .RespondJson(new User(2, "Second", "second@example.com"));          // then this

using var client = mock.CreateClient(configure: b => b.Retry(1, TimeSpan.Zero));
var api = new UsersApi(client);

var first = await api.Get(1);
var second = await api.Get(2);   // retried after the 503

mock.VerifyAllCalled();
Console.WriteLine(mock.Requests.Count);   // 3
```

- **Matching:** routes match on method, path (a trailing `*` is a wildcard), `WithQuery`, `WithHeader`, `WithBody`, `WithJsonBody<T>(predicate)` or `Matching(predicate)`.
- **Answers:** `Respond`, `RespondJson`, `RespondWith`, `Throws` and `Delay`. Successive answers form a sequence.
- **Verification:** `Times(n)` with `VerifyAllCalled()`.
- **Unmatched requests:** throw `MockHttpException`, which lists the registered routes.

## Native AOT and trimming

Nexar is `IsAotCompatible`. Members that use reflection-based JSON are annotated, so trimmed and AOT apps get a warning. Each has a source-generated counterpart:

```csharp
[JsonSerializable(typeof(User))]
partial class AppJsonContext : JsonSerializerContext;

var user = await client.Post("/users")
    .Json(newUser, AppJsonContext.Default.User)    // instead of .Json(newUser)
    .Send()
    .Json(AppJsonContext.Default.User);            // instead of .Json<User>()
```

The same applies to `JsonLines`, `JsonStream`, `Paginate`, `NexarException.Json` and `NexarProblemDetails.Extension`. `Query()` and `Form()` accept key/value pairs without reflection. [`samples/Nexar.AotSmoke`](samples/Nexar.AotSmoke) publishes a native binary with warnings as errors.

## Running the examples

```bash
dotnet run --project samples/Nexar.Examples              # all examples
dotnet run --project samples/Nexar.Examples -- caching   # just one
```

The examples start a local demo API ([`DemoApi.cs`](samples/Nexar.Examples/DemoApi.cs)), so they need no network access.

## Migrating from 2026.3001.1 and earlier

Version 2026.1008.0 replaces the whole API; see the [changelog](CHANGELOG.md).

| 2026.3001.1 | 2026.1008.0 |
|---|---|
| `Nexar.Get<User>(url)` | `new NexarClient().Get(url).Send().Json<User>()` |
| `Nexar.Create(new NexarConfig { BaseUrl = ..., TimeoutMs = ... })` | `NexarClient.Builder().BaseUrl(...).Timeout(...).Build()` |
| `api.GetAsync<User>(url, headers)` | `client.Get(url).Headers(headers).Send().Json<User>()` |
| `api.PostAsync<User>(url, body)` | `client.Post(url).Json(body).Send().Json<User>()` |
| `ContentType.FormUrlEncoded` / `FormData` / `Binary` | `.Form(...)` / `.Multipart(...)` / `.Body(...)` |
| `.Request().Url(url).WithHeader(...).WithQuery(...)` | `client.Get(url).Header(...).Query(...)` |
| `AuthHelper.Bearer(token)` / `Basic(...)` / `ApiKey(...)` | `.BearerAuth(token)`, `.BasicAuth(...)`, `.Auth(Auth.ApiKeyHeader(...))` |
| `response.IsSuccess`, `response.Data` | `res.IsSuccess`, `await res.Json<T>()` |
| `response.ErrorMessage`, a fake `Status = 500` on failure | `NexarException` with `Kind` |
| `NexarConfig.MaxRetryAttempts` | `.Retry(maxRetries)` (idempotent requests only) |
| `IInterceptor` | `DelegatingHandler` via `.AddHandler(...)` |
| `NexarConfig.ValidateSslCertificates = false` | `.DangerAcceptInvalidCerts()` |

New defaults:

- **JSON:** camelCase (`JsonSerializerDefaults.Web`). To keep PascalCase, use `.JsonOptions(o => o.PropertyNamingPolicy = null)`.
- **Cookies:** not kept unless you call `CookieStore()`.
- **Redirects:** limited to 10.
- **Compression:** compressed responses are decoded automatically.

## Contributing

```bash
dotnet build Nexar.sln
dotnet test Nexar.Test/Nexar.Test/Nexar.Test.csproj
dotnet run --project samples/Nexar.Examples
```

The tests need no network access. Changes are tracked in [CHANGELOG.md](CHANGELOG.md).

## License

MIT
