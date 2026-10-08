using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nexar.Test;

public class AuthTests
{
    [Fact]
    public async Task ClientAuthenticatorAppliesToEveryRequest()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer("client-token")));

        await client.Get("/a").Send();
        await client.Post("/b").Send();

        Assert.All(handler.Requests, r => Assert.Equal("Bearer client-token", r.Headers["Authorization"]));
    }

    [Fact]
    public async Task RequestAuthOverridesClientAuth()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer("client-token")));

        await client.Get("/").BasicAuth("user", "pass").Send();

        Assert.StartsWith("Basic ", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task NoAuthSkipsClientAuth()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer("client-token")));

        await client.Get("/").NoAuth().Send();

        Assert.False(handler.Last.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task ExplicitAuthorizationHeaderSkipsClientAuth()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer("client-token")));

        await client.Get("/").Header("Authorization", "Custom abc").Send();

        Assert.Equal("Custom abc", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task ApiKeyHeader()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.ApiKeyHeader("X-Api-Key", "secret")));

        await client.Get("/").Send();

        Assert.Equal("secret", handler.Last.Headers["X-Api-Key"]);
    }

    [Fact]
    public async Task ApiKeyQueryKeepsExistingQuery()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.ApiKeyQuery("api_key", "s e/cret")));

        await client.Get("/items").Query("page", 2).Send();

        Assert.Equal("?page=2&api_key=s%20e%2Fcret", handler.Last.Url.Query);
    }

    [Fact]
    public async Task BearerTokenProviderIsCalledPerRequest()
    {
        var calls = 0;
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer(_ => ValueTask.FromResult($"token-{++calls}"))));

        await client.Get("/").Send();
        await client.Get("/").Send();

        Assert.Equal("Bearer token-1", handler.Requests[0].Headers["Authorization"]);
        Assert.Equal("Bearer token-2", handler.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task FailingAuthenticatorIsAuthError()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer(_ => ValueTask.FromResult(""))));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsAuth);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CustomAuthenticatorCanSignTheBody()
    {
        var key = "k"u8.ToArray();
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Custom(async (request, ct) =>
        {
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            request.Headers.Add("X-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(key, body)));
        })));

        await client.Post("/").Json(new { a = 1 }).Send();

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(key, "{\"a\":1}"u8.ToArray()));
        Assert.Equal(expected, handler.Last.Headers["X-Signature"]);
        Assert.Equal("{\"a\":1}", handler.Last.Body);
    }

    [Fact]
    public void CredentialsCannotBeCombinedWithCustomHandler()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .HttpMessageHandler(new FakeHandler())
            .Credentials(new NetworkCredential("u", "p"))
            .Build());

        Assert.True(ex.IsBuilder);
    }

    // ---- Digest -------------------------------------------------------------

    private static FakeHandler DigestServer(params string[] challenges) => new(request =>
    {
        if (request.Headers.Authorization != null)
        {
            return FakeHandler.Respond(HttpStatusCode.OK);
        }
        var response = FakeHandler.Respond(HttpStatusCode.Unauthorized);
        foreach (var challenge in challenges)
        {
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge);
        }
        return response;
    });

    [Fact]
    public async Task DigestMatchesRfc2617Example()
    {
        var handler = DigestServer(
            "Digest realm=\"testrealm@host.com\", qop=\"auth,auth-int\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"");
        using var client = TestClient.Create(handler, b => b.BaseUrl("http://www.nowhere.org")
            .Auth(new DigestAuthenticator("Mufasa", "Circle Of Life", () => "0a4f113b")));

        using var res = await client.Get("/dir/index.html").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        var authorization = handler.Last.Headers["Authorization"];
        Assert.Contains("response=\"6629fae49393a05397450978507c4ef1\"", authorization);
        Assert.Contains("nc=00000001", authorization);
        Assert.Contains("opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", authorization);
        Assert.Contains("uri=\"/dir/index.html\"", authorization);
    }

    [Fact]
    public async Task DigestPrefersSha256AndMatchesRfc7616Example()
    {
        const string common = "realm=\"http-auth@example.org\", qop=\"auth, auth-int\", nonce=\"7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v\", opaque=\"FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS\"";
        var handler = DigestServer($"Digest {common}, algorithm=MD5", $"Digest {common}, algorithm=SHA-256");
        using var client = TestClient.Create(handler, b => b.BaseUrl("https://www.example.org")
            .Auth(new DigestAuthenticator("Mufasa", "Circle of Life", () => "f2/wE4q74E6zIJEtWaHKaf5wv/H5QzzpXusqGemxURZJ")));

        await client.Get("/dir/index.html").Send();

        var authorization = handler.Last.Headers["Authorization"];
        Assert.Contains("algorithm=SHA-256", authorization);
        Assert.Contains("response=\"753927fa0e85d155564e2e272a28d1802ca10daf4496794697cf8db5856cb6c1\"", authorization);
    }

    [Fact]
    public async Task DigestAuthenticatesLaterRequestsUpFront()
    {
        var handler = DigestServer("Digest realm=\"r\", qop=\"auth\", nonce=\"n1\"");
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Digest("u", "p")));

        await client.Get("/first").Send();
        await client.Get("/second").Send();

        Assert.Equal(3, handler.Requests.Count);   // 401, 200, then 200 without a new challenge
        Assert.Contains("nc=00000002", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task DigestWithWrongCredentialsReturnsThe401()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Digest realm=\"r\", qop=\"auth\", nonce=\"n1\"");
            return response;
        });
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Digest("u", "wrong")));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UnauthorizedResendIsSkippedForStreamBodies()
    {
        var handler = DigestServer("Digest realm=\"r\", nonce=\"n1\"");
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Digest("u", "p")));

        using var res = await client.Put("/").Body(new MemoryStream("x"u8.ToArray())).Send();

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Single(handler.Requests);
    }

    // ---- OAuth2 client credentials ------------------------------------------

    private sealed class OAuthServer
    {
        public int TokenRequests;
        public string? LastTokenAuthorization;
        public string? LastTokenBody;
        public Func<int, HttpResponseMessage>? TokenResponse;
        public Func<string?, HttpStatusCode> ApiStatus = _ => HttpStatusCode.OK;

        public FakeHandler Handler => new(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                var n = Interlocked.Increment(ref TokenRequests);
                LastTokenAuthorization = request.Headers.Authorization?.ToString();
                LastTokenBody = await request.Content!.ReadAsStringAsync(ct);
                await Task.Delay(20, ct);
                return TokenResponse?.Invoke(n)
                    ?? FakeHandler.Respond(HttpStatusCode.OK, $"{{\"access_token\":\"token-{n}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}");
            }
            return FakeHandler.Respond(ApiStatus(request.Headers.Authorization?.Parameter));
        });
    }

    private static NexarClient OAuthClient(FakeHandler handler, Func<NexarClient, OAuth2ClientCredentialsOptions> options)
    {
        var tokenClient = TestClient.Create(handler);
        return TestClient.Create(handler, b => b.Auth(Auth.OAuth2ClientCredentials(options(tokenClient))));
    }

    private static OAuth2ClientCredentialsOptions Options(NexarClient tokenClient, TimeProvider? clock = null, bool inBody = false) => new()
    {
        TokenUrl = "https://api.test/token",
        ClientId = "my client",
        ClientSecret = "s3cret",
        Scope = "read write",
        TokenClient = tokenClient,
        SendCredentialsInBody = inBody,
        TimeProvider = clock ?? TimeProvider.System
    };

    [Fact]
    public async Task OAuth2FetchesTokenWithBasicClientAuth()
    {
        var server = new OAuthServer();
        var handler = server.Handler;
        using var client = OAuthClient(handler, tc => Options(tc));

        await client.Get("/data").Send();

        Assert.Equal("Bearer token-1", handler.Last.Headers["Authorization"]);
        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes("my+client:s3cret"));
        Assert.Equal($"Basic {expectedBasic}", server.LastTokenAuthorization);
        Assert.Equal("grant_type=client_credentials&scope=read+write", server.LastTokenBody);
    }

    [Fact]
    public async Task OAuth2CanSendCredentialsInBody()
    {
        var server = new OAuthServer();
        using var client = OAuthClient(server.Handler, tc => Options(tc, inBody: true));

        await client.Get("/data").Send();

        Assert.Null(server.LastTokenAuthorization);
        Assert.Contains("client_id=my+client&client_secret=s3cret", server.LastTokenBody);
    }

    [Fact]
    public async Task OAuth2FetchesOneTokenForConcurrentRequests()
    {
        var server = new OAuthServer();
        using var client = OAuthClient(server.Handler, tc => Options(tc));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.Get("/data").Send()));

        Assert.Equal(1, server.TokenRequests);
    }

    [Fact]
    public async Task OAuth2RefreshesTokenBeforeExpiry()
    {
        var clock = new ManualClock();
        var server = new OAuthServer();
        var handler = server.Handler;
        using var client = OAuthClient(handler, tc => Options(tc, clock));

        await client.Get("/data").Send();
        clock.Advance(TimeSpan.FromSeconds(3500));   // still more than 30 s left
        await client.Get("/data").Send();
        Assert.Equal(1, server.TokenRequests);

        clock.Advance(TimeSpan.FromSeconds(80));     // inside the 30 s refresh window
        await client.Get("/data").Send();

        Assert.Equal(2, server.TokenRequests);
        Assert.Equal("Bearer token-2", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task OAuth2RefreshesTokenAfter401WithoutUsingARetry()
    {
        var server = new OAuthServer { ApiStatus = token => token == "token-1" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK };
        var handler = server.Handler;
        using var client = OAuthClient(handler, tc => Options(tc));

        using var res = await client.Post("/data").Json(new { a = 1 }).Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, server.TokenRequests);
        Assert.Equal("Bearer token-2", handler.Last.Headers["Authorization"]);
        Assert.Equal("{\"a\":1}", handler.Last.Body);
    }

    [Fact]
    public async Task OAuth2ReSendsOnlyOnceOnRepeated401()
    {
        var server = new OAuthServer { ApiStatus = _ => HttpStatusCode.Unauthorized };
        var handler = server.Handler;
        using var client = OAuthClient(handler, tc => Options(tc));

        using var res = await client.Get("/data").Send();

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(2, handler.Requests.Count(r => r.Url.AbsolutePath == "/data"));
    }

    [Fact]
    public async Task OAuth2TokenEndpointErrorIsAuthError()
    {
        var server = new OAuthServer
        {
            TokenResponse = _ => FakeHandler.Respond(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\",\"error_description\":\"Unknown client\"}")
        };
        using var client = OAuthClient(server.Handler, tc => Options(tc));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/data").Send());

        Assert.True(ex.IsAuth);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("invalid_client (Unknown client)", ex.Message);
    }

    [Fact]
    public async Task OAuth2TokenEndpointConnectionFailureIsAuthError()
    {
        var handler = new FakeHandler((request, _) => request.RequestUri!.AbsolutePath == "/token"
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : Task.FromResult(FakeHandler.Respond(HttpStatusCode.OK)));
        using var client = OAuthClient(handler, tc => Options(tc));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/data").Send());

        Assert.True(ex.IsAuth);
        Assert.IsType<NexarException>(ex.InnerException);
    }

    [Fact]
    public async Task OAuth2MissingAccessTokenIsAuthError()
    {
        var server = new OAuthServer { TokenResponse = _ => FakeHandler.Respond(HttpStatusCode.OK, "{\"token_type\":\"Bearer\"}") };
        using var client = OAuthClient(server.Handler, tc => Options(tc));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/data").Send());

        Assert.True(ex.IsAuth);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
