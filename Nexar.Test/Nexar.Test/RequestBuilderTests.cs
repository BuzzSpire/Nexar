using System.Text;

namespace Nexar.Test;

public class RequestBuilderTests
{
    [Theory]
    [InlineData("https://api.test", "/users", "https://api.test/users")]
    [InlineData("https://api.test/", "users", "https://api.test/users")]
    [InlineData("https://api.test/v1/", "/users/1", "https://api.test/v1/users/1")]
    [InlineData("https://api.test/v1", "", "https://api.test/v1")]
    public async Task JoinsRelativeUrlsToBaseUrl(string baseUrl, string path, string expected)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.BaseUrl(baseUrl));

        await client.Get(path).Send();

        Assert.Equal(expected, handler.Last.Url.ToString());
    }

    [Fact]
    public async Task AbsoluteUrlBypassesBaseUrl()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("https://other.test/x").Send();

        Assert.Equal("https://other.test/x", handler.Last.Url.ToString());
    }

    [Fact]
    public async Task UsesTheRequestedMethod()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").Send();
        await client.Post("/").Send();
        await client.Put("/").Send();
        await client.Patch("/").Send();
        await client.Delete("/").Send();
        await client.Head("/").Send();
        await client.Request(HttpMethod.Options, "/").Send();

        Assert.Equal(
            new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete, HttpMethod.Head, HttpMethod.Options },
            handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task EncodesQueryParameters()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/search")
            .Query("q", "a b&c")
            .Query("limit", 10)
            .Query("price", 1.5)
            .Query("exact", true)
            .Query("skipped", null)
            .Send();

        Assert.Equal("?q=a%20b%26c&limit=10&price=1.5&exact=true", handler.Last.Url.Query);
    }

    [Fact]
    public async Task AppendsQueryToExistingQueryString()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/search?a=1").Query("b", 2).Send();

        Assert.Equal("?a=1&b=2", handler.Last.Url.Query);
    }

    [Fact]
    public async Task QueryFromObjectUsesJsonNamingAndRepeatsArrays()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/items")
            .Query(new { PageSize = 20, Ids = new[] { 1, 2 }, Filter = (string?)null })
            .Send();

        Assert.Equal("?pageSize=20&ids=1&ids=2", handler.Last.Url.Query);
    }

    [Fact]
    public async Task QueryFromDictionary()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/items").Query(new Dictionary<string, string> { ["a"] = "1" }).Send();

        Assert.Equal("?a=1", handler.Last.Url.Query);
    }

    [Fact]
    public async Task RequestHeadersOverrideDefaultHeaders()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b
            .DefaultHeader("X-Env", "default")
            .DefaultHeader("X-Keep", "kept")
            .UserAgent("nexar-tests"));

        await client.Get("/").Header("x-env", "override").Send();

        Assert.Equal("override", handler.Last.Headers["X-Env"]);
        Assert.Equal("kept", handler.Last.Headers["X-Keep"]);
        Assert.Equal("nexar-tests", handler.Last.Headers["User-Agent"]);
    }

    [Fact]
    public async Task BearerAuth()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").BearerAuth("token123").Send();

        Assert.Equal("Bearer token123", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task BasicAuth()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").BasicAuth("user", "pass").Send();

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Assert.Equal($"Basic {expected}", handler.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task JsonBodyUsesCamelCaseByDefault()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/users").Json(new { FirstName = "Ada" }).Send();

        Assert.Equal("{\"firstName\":\"Ada\"}", handler.Last.Body);
        Assert.Equal("application/json; charset=utf-8", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task JsonOptionsCanBeCustomized()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.JsonOptions(o => o.PropertyNamingPolicy = null));

        await client.Post("/users").Json(new { FirstName = "Ada" }).Send();

        Assert.Equal("{\"FirstName\":\"Ada\"}", handler.Last.Body);
    }

    [Fact]
    public async Task FormBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/login").Form(new { UserName = "ada", Remember = true }).Send();

        Assert.Equal("userName=ada&remember=true", handler.Last.Body);
        Assert.Equal("application/x-www-form-urlencoded", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task MultipartBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/upload")
            .Multipart(new MultipartForm()
                .Text("title", "Holiday")
                .File("photo", new byte[] { 1, 2, 3 }, "beach.jpg", "image/jpeg"))
            .Send();

        Assert.StartsWith("multipart/form-data", handler.Last.ContentHeaders["Content-Type"]);
        Assert.Contains("name=title", handler.Last.Body);
        Assert.Contains("Holiday", handler.Last.Body);
        Assert.Contains("filename=beach.jpg", handler.Last.Body);
        Assert.Contains("image/jpeg", handler.Last.Body);
    }

    [Fact]
    public async Task TextBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/notes").Body("hello").Send();

        Assert.Equal("hello", handler.Last.Body);
        Assert.Equal("text/plain; charset=utf-8", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task BinaryBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Put("/blob").Body(Encoding.UTF8.GetBytes("raw")).Send();

        Assert.Equal("raw", handler.Last.Body);
        Assert.Equal("application/octet-stream", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task StreamBody()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Put("/blob").Body(new MemoryStream(Encoding.UTF8.GetBytes("streamed")), "text/csv").Send();

        Assert.Equal("streamed", handler.Last.Body);
        Assert.Equal("text/csv", handler.Last.ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task ContentTypeHeaderOverridesBodyContentType()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Post("/").Json(new { a = 1 }).Header("Content-Type", "application/vnd.api+json").Send();

        Assert.Equal("application/vnd.api+json", handler.Last.ContentHeaders["Content-Type"]);
        Assert.False(handler.Last.Headers.ContainsKey("Content-Type"));
    }
}
