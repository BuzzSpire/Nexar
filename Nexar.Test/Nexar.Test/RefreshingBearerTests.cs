using System.Net;

namespace Nexar.Test;

public class RefreshingBearerTests
{
    /// <summary>A token cache like an app would write: refreshes only when asked to.</summary>
    private sealed class TokenCache
    {
        private int _version = 1;
        public List<bool> Calls { get; } = new();

        public ValueTask<string> GetAsync(bool forceRefresh, CancellationToken _)
        {
            Calls.Add(forceRefresh);
            if (forceRefresh)
            {
                _version++;
            }
            return ValueTask.FromResult($"token-{_version}");
        }
    }

    [Fact]
    public async Task RefreshesOnceAfter401AndResends()
    {
        var tokens = new TokenCache();
        var handler = new FakeHandler(request =>
            FakeHandler.Respond(request.Headers.Authorization?.Parameter == "token-1" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer(tokens.GetAsync)));

        using var res = await client.Get("/me").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(new[] { false, true, false }, tokens.Calls);
        Assert.Equal(new[] { "Bearer token-1", "Bearer token-2" }, handler.Requests.Select(r => r.Headers["Authorization"]));
    }

    [Fact]
    public async Task NoRefreshWhenTheTokenWorks()
    {
        var tokens = new TokenCache();
        using var client = TestClient.Create(new FakeHandler(), b => b.Auth(Auth.Bearer(tokens.GetAsync)));

        await client.Get("/a").Send();
        await client.Get("/b").Send();

        Assert.Equal(new[] { false, false }, tokens.Calls);
    }

    [Fact]
    public async Task Repeated401ReturnsTheResponseAfterOneRefresh()
    {
        var tokens = new TokenCache();
        var handler = new FakeHandler(HttpStatusCode.Unauthorized);
        using var client = TestClient.Create(handler, b => b.Auth(Auth.Bearer(tokens.GetAsync)));

        using var res = await client.Get("/").Send();

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(tokens.Calls, c => c);
    }

    [Fact]
    public async Task FailingRefreshIsAuthError()
    {
        using var client = TestClient.Create(new FakeHandler(HttpStatusCode.Unauthorized), b => b.Auth(Auth.Bearer((force, _) =>
            force ? throw new InvalidOperationException("refresh token expired") : ValueTask.FromResult("t"))));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsAuth);
        Assert.Contains("refresh token expired", ex.Message);
    }
}
