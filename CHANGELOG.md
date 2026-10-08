# Changelog

All notable changes to Nexar are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions use the `YEAR.MMDD.N` scheme: the release date as month and day, and `N` counting additional releases on the same day.

## [Unreleased]

### Added

- A per-host circuit breaker (`CircuitBreaker(...)`, `ErrorKind.CircuitOpen`).
- Parallel chunked downloads (`DownloadTo(path, DownloadOptions)`).
- Request and response hooks (`OnRequest`, `OnResponse`).
- Streaming request bodies: `JsonLines(IAsyncEnumerable<T>)` (NDJSON) and `JsonStreamed(value)`.
- TLS certificate pinning (`PinCertificate`, `ClientBuilder.ComputePin`).
- OAuth 2.0 for signed-in users: `OAuth2.CreatePkce()`, `OAuth2.ExchangeCodeAsync()`, `OAuth2.RefreshAsync()` and `Auth.OAuth2RefreshToken()` with refresh token rotation.
- Pluggable body formats (`IContentSerializer`, `ClientBuilder.Serializer`, `Body(value, serializer)`, `Serialized(value)`, `As<T>()`) with XML built in (`XmlContentSerializer`).
- Client-level default query parameters (`DefaultQuery`), and `NexarClient.With(...)` to derive clients with other defaults that share the connection pool.
- Retry jitter (`Retry(..., jitter: true)`), custom retry conditions (`RetryWhen`), a retry callback (`OnRetry`) and `IdempotencyKey()` for safely retried POSTs.

## [2026.1008.0] - 2026-10-08

A complete rewrite with a fluent API modeled after Rust's reqwest. **This release is not compatible with earlier versions**; see [Migrating from 2026.3001.1 and earlier](README.md#migrating-from-202630011-and-earlier).

### Added

- `NexarClient` and `ClientBuilder` (one reusable client), `RequestBuilder` (method chaining, finished with `Send()`), `NexarResponse` (body read on demand as `Text`, `Bytes`, `Json<T>`, `Stream`), and `NexarException` with `ErrorKind`.
- Request features: path parameters, typed `Accept`/`Accept-Language`, conditional requests (`IfNoneMatch`, `IfMatch`, `IfModifiedSince`), ranges, repeated headers, `Json`/`Form`/`Multipart`/`File`/raw bodies, request compression, query and form styles, custom methods (`PROPFIND`, `QUERY`, ...), `Build()` + `Execute()` for signing, `TryClone()`.
- Response features: typed headers, `Links` (RFC 8288), trailers, problem details (RFC 9457), charset handling, `SaveTo`, resumable `DownloadTo`, Server-Sent Events, NDJSON, JSON array streaming, pagination, upload and download progress, size limits.
- Authentication: `IAuthenticator` with Bearer (static, provider, refreshing), Basic, API keys, Digest (RFC 7616), OAuth 2.0 client credentials, custom signing, and NTLM/Negotiate via `Credentials()`.
- Resilience: retries for idempotent requests with exponential backoff and `Retry-After`, timeouts covering the body, client-side rate limiting, an RFC 9111 HTTP cache.
- Connections: redirect policy, proxies (HTTP and SOCKS), cookie store, client certificates and custom root CAs, connection pool settings, HTTP/2 and HTTP/3 tuning, Unix sockets, named pipes, DNS overrides.
- Observability: `ActivitySource` and `Meter` named `Nexar` (OpenTelemetry HTTP conventions), `ILogger` logging, secret redaction.
- Native AOT and trimming support, with `JsonTypeInfo<T>` overloads.
- Targets .NET 10 (LTS); .NET 9 support ended in May 2026.
- New packages: `BuzzSpire.Nexar.Testing` (`MockHttp`) and `BuzzSpire.Nexar.Extensions.DependencyInjection` (`AddNexarClient`).

### Changed

- JSON uses `JsonSerializerDefaults.Web` (camelCase, case-insensitive) by default.
- Failures throw `NexarException` instead of returning a made-up `500` response; 4xx/5xx responses throw only through `ErrorForStatus()`.
- Gzip, deflate and Brotli responses are decompressed automatically.
- Redirects are limited to 10 hops, and exceeding the limit throws `ErrorKind.Redirect`.
- Cookies are not kept unless `CookieStore()` is used.

### Removed

- The 2026.3001.1 API: the `Nexar` class with its static and instance methods, `NexarConfig`, `RequestOptions`, `ContentType`, `NexarResponse<T>`, `NexarRequestBuilder`, `IInterceptor` and `AuthHelper`.

## [2026.3001.1] - 2026-01-30

### Changed

- POST, PUT and PATCH use a single generic type `T` instead of `TRequest`/`TResponse`, with the parameter order url, body, headers, contentType.

## [2026.3001.0] - 2026-01-30

### Changed

- Simplified the generic request APIs.

## [2026.2901.0] - 2026-01-29

### Added

- Form data, form URL-encoded and binary content types.
- Interface support and request builder integration for content types.

## [2025.1214.0] - 2025-12-14

### Added

- Same features as 2.0.0, published under the date-based version scheme.

## [2.0.0] - 2025-12-14

### Added

- Static methods (`Nexar.Get<T>()`, ...), typed responses, interceptors, retries with exponential backoff, a fluent request builder and authentication helpers.
- Non-generic static method overloads.

## [2024.1012.0] - 2024-10-12

### Fixed

- Request bodies for POST, PUT, DELETE and PATCH.

## [1.0.0] - 2024-10-08

### Added

- First release: asynchronous GET, POST, PUT, DELETE and PATCH requests.

[2026.1008.0]: https://github.com/BuzzSpire/Nexar/compare/2025.1214.0...main
[2026.3001.1]: https://www.nuget.org/packages/BuzzSpire.Nexar/2026.3001.1
[2026.3001.0]: https://www.nuget.org/packages/BuzzSpire.Nexar/2026.3001.0
[2026.2901.0]: https://www.nuget.org/packages/BuzzSpire.Nexar/2026.2901.0
[2025.1214.0]: https://github.com/BuzzSpire/Nexar/releases/tag/2025.1214.0
[2.0.0]: https://www.nuget.org/packages/BuzzSpire.Nexar/2.0.0
[2024.1012.0]: https://github.com/BuzzSpire/Nexar/releases/tag/2024.1012.0
[1.0.0]: https://www.nuget.org/packages/BuzzSpire.Nexar/1.0.0
