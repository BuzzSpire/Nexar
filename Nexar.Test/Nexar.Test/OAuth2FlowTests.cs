using System.Net;
using System.Text;
using Nexar.Testing;

namespace Nexar.Test;

public class OAuth2FlowTests
{
    private const string TokenUrl = "https://login.test/token";

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static string Form(MockRequest request, string name) =>
        System.Web.HttpUtility.ParseQueryString(request.Body ?? "")[name] ?? "";

    // ---- PKCE and the code exchange ---------------------------------------------------

    [Fact]
    public void PkceMatchesRfc7636AppendixB()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuth2.S256Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void CreatePkceMakesAValidPair()
    {
        var pkce = OAuth2.CreatePkce();

        Assert.Equal("S256", pkce.Method);
        Assert.Equal(43, pkce.Verifier.Length);
        Assert.DoesNotContain('=', pkce.Verifier + pkce.Challenge);
        Assert.Equal(OAuth2.S256Challenge(pkce.Verifier), pkce.Challenge);
        Assert.NotEqual(pkce.Verifier, OAuth2.CreatePkce().Verifier);
    }

    [Fact]
    public async Task ExchangeCodeAsPublicClient()
    {
        var mock = new MockHttp();
        mock.OnPost("/token").RespondWith(_ => FakeHandler.Respond(HttpStatusCode.OK,
            "{\"access_token\":\"at-1\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"rt-1\",\"scope\":\"read\",\"id_token\":\"id-1\"}"));
        using var client = mock.CreateClient();

        var tokens = await OAuth2.ExchangeCodeAsync(client, TokenUrl, "desktop-app", "the-code", "the-verifier", "http://localhost:8765/callback");

        var request = mock.Requests.Single();
        Assert.Equal("authorization_code", Form(request, "grant_type"));
        Assert.Equal("the-code", Form(request, "code"));
        Assert.Equal("the-verifier", Form(request, "code_verifier"));
        Assert.Equal("http://localhost:8765/callback", Form(request, "redirect_uri"));
        Assert.Equal("desktop-app", Form(request, "client_id"));
        Assert.Null(request.Header("Authorization"));
        Assert.Equal(("at-1", "rt-1", "read", "id-1", "Bearer"), (tokens.AccessToken, tokens.RefreshToken, tokens.Scope, tokens.IdToken, tokens.TokenType));
        Assert.InRange(tokens.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));
    }

    [Fact]
    public async Task ExchangeCodeAsConfidentialClientUsesBasicAuth()
    {
        var mock = new MockHttp();
        mock.OnPost("/token").RespondJson(new { access_token = "at" });
        using var client = mock.CreateClient();

        await OAuth2.ExchangeCodeAsync(client, TokenUrl, "web-app", "code", codeVerifier: null, "https://app.test/cb", clientSecret: "s3cret");

        var request = mock.Requests.Single();
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("web-app:s3cret"))}", request.Header("Authorization"));
        Assert.Equal("", Form(request, "client_id"));
        Assert.Equal("", Form(request, "code_verifier"));
    }

    [Fact]
    public async Task RefreshAsync()
    {
        var mock = new MockHttp();
        mock.OnPost("/token").RespondJson(new { access_token = "at-2", refresh_token = "rt-2" });
        using var client = mock.CreateClient();

        var tokens = await OAuth2.RefreshAsync(client, TokenUrl, "app", "rt-1", scope: "read write");

        Assert.Equal("refresh_token", Form(mock.Requests[0], "grant_type"));
        Assert.Equal("rt-1", Form(mock.Requests[0], "refresh_token"));
        Assert.Equal("read write", Form(mock.Requests[0], "scope"));
        Assert.Equal("rt-2", tokens.RefreshToken);
    }

    // ---- The refresh token authenticator ------------------------------------------

    /// <summary>A token endpoint that issues at-N / rt-N and rotates the refresh token, plus an API that checks the token.</summary>
    private sealed class Server
    {
        private int _issued;
        public bool Rotate = true;
        public string? ValidRefreshToken = "rt-0";
        public Func<string?, HttpStatusCode> Api = _ => HttpStatusCode.OK;
        public MockHttp Mock { get; } = new();
        public List<string> RefreshTokensSeen { get; } = new();

        public Server()
        {
            Mock.OnPost("/token").RespondWith(request =>
            {
                var presented = Form(request, "refresh_token");
                lock (RefreshTokensSeen)
                {
                    RefreshTokensSeen.Add(presented);
                }
                if (presented != ValidRefreshToken)
                {
                    return FakeHandler.Respond(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Token expired\"}");
                }
                var n = Interlocked.Increment(ref _issued);
                Thread.Sleep(20);
                if (Rotate)
                {
                    ValidRefreshToken = $"rt-{n}";
                    return FakeHandler.Respond(HttpStatusCode.OK, $"{{\"access_token\":\"at-{n}\",\"expires_in\":3600,\"refresh_token\":\"rt-{n}\"}}");
                }
                return FakeHandler.Respond(HttpStatusCode.OK, $"{{\"access_token\":\"at-{n}\",\"expires_in\":3600}}");
            });
            Mock.OnGet("/api").RespondWith(request => FakeHandler.Respond(Api(request.Header("Authorization"))));
        }
    }

    private static NexarClient Client(Server server, ManualClock clock, List<OAuth2Tokens>? saved = null, OAuth2Tokens? initial = null)
    {
        var tokenClient = server.Mock.CreateClient("https://login.test");
        return server.Mock.CreateClient("https://api.test", b => b.Auth(Auth.OAuth2RefreshToken(new OAuth2RefreshTokenOptions
        {
            TokenUrl = TokenUrl,
            ClientId = "app",
            RefreshToken = "rt-0",
            InitialTokens = initial,
            TokenClient = tokenClient,
            TimeProvider = clock,
            OnTokensRefreshed = (tokens, _) =>
            {
                saved?.Add(tokens);
                return ValueTask.CompletedTask;
            }
        })));
    }

    [Fact]
    public async Task RefreshesOnFirstUseAndCachesUntilExpiry()
    {
        var server = new Server();
        var clock = new ManualClock();
        using var client = Client(server, clock);

        await client.Get("/api").Send();
        clock.Advance(TimeSpan.FromMinutes(30));
        await client.Get("/api").Send();

        Assert.Equal(new[] { "rt-0" }, server.RefreshTokensSeen);
        Assert.All(server.Mock.Requests.Where(r => r.Url.AbsolutePath == "/api"), r => Assert.Equal("Bearer at-1", r.Header("Authorization")));
    }

    [Fact]
    public async Task InitialTokensAreUsedWithoutRefreshing()
    {
        var server = new Server();
        var clock = new ManualClock();
        var initial = new OAuth2Tokens("at-initial", "Bearer", clock.GetUtcNow().AddHours(1), "rt-0", null, null);
        using var client = Client(server, clock, initial: initial);

        await client.Get("/api").Send();

        Assert.Empty(server.RefreshTokensSeen);
        Assert.Equal("Bearer at-initial", server.Mock.Requests.Single().Header("Authorization"));
    }

    [Fact]
    public async Task RotatedRefreshTokensArePersistedAndUsed()
    {
        var server = new Server();
        var clock = new ManualClock();
        var saved = new List<OAuth2Tokens>();
        using var client = Client(server, clock, saved);

        await client.Get("/api").Send();
        clock.Advance(TimeSpan.FromHours(2));   // expired: refresh again with the rotated token
        await client.Get("/api").Send();

        Assert.Equal(new[] { "rt-0", "rt-1" }, server.RefreshTokensSeen);
        Assert.Equal(new[] { "rt-1", "rt-2" }, saved.Select(t => t.RefreshToken));
    }

    [Fact]
    public async Task WithoutRotationTheOldRefreshTokenIsKept()
    {
        var server = new Server { Rotate = false };
        var clock = new ManualClock();
        var saved = new List<OAuth2Tokens>();
        using var client = Client(server, clock, saved);

        await client.Get("/api").Send();
        clock.Advance(TimeSpan.FromHours(2));
        await client.Get("/api").Send();

        Assert.Equal(new[] { "rt-0", "rt-0" }, server.RefreshTokensSeen);
        Assert.All(saved, t => Assert.Equal("rt-0", t.RefreshToken));
    }

    [Fact]
    public async Task UnauthorizedTriggersOneRefreshAndResend()
    {
        var server = new Server { Api = auth => auth == "Bearer at-1" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK };
        var clock = new ManualClock();
        using var client = Client(server, clock);

        using var res = await client.Get("/api").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(new[] { "rt-0", "rt-1" }, server.RefreshTokensSeen);
    }

    [Fact]
    public async Task InvalidGrantIsAuthError()
    {
        var server = new Server { ValidRefreshToken = "something-else" };
        using var client = Client(server, new ManualClock());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/api").Send());

        Assert.True(ex.IsAuth);
        Assert.Contains("invalid_grant (Token expired)", ex.Message);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneRefresh()
    {
        var server = new Server();
        using var client = Client(server, new ManualClock());

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ => { using var r = await client.Get("/api").Send(); }));

        Assert.Single(server.RefreshTokensSeen);
    }
}
