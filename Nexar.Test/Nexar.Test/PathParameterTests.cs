namespace Nexar.Test;

public class PathParameterTests
{
    [Fact]
    public async Task EscapesValuesAsPathSegments()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/users/{user}/repos/{repo}")
            .Path("user", "ada lovelace")
            .Path("repo", "a/b?c#d")
            .Send();

        Assert.Equal("/users/ada%20lovelace/repos/a%2Fb%3Fc%23d", handler.Last.Url.AbsolutePath);
        Assert.Equal("", handler.Last.Url.Query);
        Assert.Equal("", handler.Last.Url.Fragment);
    }

    [Fact]
    public async Task FormatsValuesWithInvariantCulture()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);
        var id = Guid.Parse("8f14e45f-ceea-467f-a8f2-5b6f6a0f0d3c");

        await client.Get("/items/{id}/v/{version}").Path("id", id).Path("version", 1.5).Send();

        Assert.Equal("/items/8f14e45f-ceea-467f-a8f2-5b6f6a0f0d3c/v/1.5", handler.Last.Url.AbsolutePath);
    }

    [Fact]
    public async Task WorksWithQueryAndBaseUrlPath()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.BaseUrl("https://api.test/v1/"));

        await client.Get("/users/{id}").Path("id", 7).Query("expand", "posts").Send();

        Assert.Equal("https://api.test/v1/users/7?expand=posts", handler.Last.Url.AbsoluteUri);
    }

    [Fact]
    public async Task UnicodeIsPercentEncoded()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/cities/{name}").Path("name", "İstanbul").Send();

        Assert.Equal("/cities/%C4%B0stanbul", handler.Last.Url.AbsolutePath);
    }

    [Fact]
    public async Task MissingValueIsBuilderError()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/users/{user}/repos/{repo}").Path("user", "ada").Send());

        Assert.True(ex.IsBuilder);
        Assert.Contains("{repo}", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnusedValueIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/users/{user}").Path("user", "ada").Path("usr", "typo").Send());

        Assert.True(ex.IsBuilder);
        Assert.Contains("usr", ex.Message);
    }

    [Fact]
    public async Task NullValueIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/users/{user}").Path("user", null!).Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task UrlsWithoutPathCallsAreLeftAlone()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/search").Query("q", "{literal}").Send();

        Assert.Equal("?q=%7Bliteral%7D", handler.Last.Url.Query);
    }

    [Fact]
    public async Task SamePlaceholderCanAppearTwice()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/{v}/compare/{v}").Path("v", "x y").Send();

        Assert.Equal("/x%20y/compare/x%20y", handler.Last.Url.AbsolutePath);
    }
}
