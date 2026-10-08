using System.Net;
using System.Net.Http.Headers;

namespace Nexar.Test;

public class TypedHeaderTests
{
    [Fact]
    public async Task AcceptAndAcceptLanguage()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/")
            .Accept("application/json", "text/csv;q=0.5")
            .AcceptLanguage("tr-TR", "en;q=0.8")
            .Send();

        Assert.Equal("application/json, text/csv; q=0.5", handler.Last.Headers["Accept"]);
        Assert.Equal("tr-TR, en; q=0.8", handler.Last.Headers["Accept-Language"]);
    }

    [Fact]
    public async Task AcceptReplacesDefaultAccept()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b.DefaultHeader("Accept", "text/html"));

        await client.Get("/").Accept("application/json").Send();

        Assert.Equal("application/json", handler.Last.Headers["Accept"]);
    }

    [Theory]
    [InlineData("not a media type")]
    [InlineData("application/json;q=abc")]
    public async Task InvalidAcceptIsBuilderError(string value)
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Accept(value).Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task EmptyAcceptIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Accept().Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task TypedResponseHeaders()
    {
        var lastModified = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.Created, "{}");
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"", isWeak: true);
            response.Headers.Location = new Uri("/users/42", UriKind.Relative);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            response.Headers.Add("X-RateLimit-Remaining", "99");
            response.Content.Headers.LastModified = lastModified;
            return response;
        });
        using var client = TestClient.Create(handler, b => b.BaseUrl("https://api.test/v1/"));

        using var res = await client.Post("users").Send();

        Assert.Equal("application/json", res.ContentType!.MediaType);
        Assert.Equal("utf-8", res.ContentType.CharSet);
        Assert.Equal("\"v2\"", res.ETag!.Tag);
        Assert.True(res.ETag.IsWeak);
        Assert.Equal(lastModified, res.LastModified);
        Assert.Equal("https://api.test/users/42", res.Location!.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(120), res.RetryAfter);
        Assert.Equal("99", res.Header("x-ratelimit-remaining"));
        Assert.StartsWith("application/json", res.Header("Content-Type"));
    }

    [Fact]
    public async Task MissingTypedHeadersAreNull()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();

        Assert.Null(res.ETag);
        Assert.Null(res.LastModified);
        Assert.Null(res.Location);
        Assert.Null(res.RetryAfter);
        Assert.Null(res.Header("X-Missing"));
    }

    [Fact]
    public async Task RetryAfterDate()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2));
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();

        Assert.InRange(res.RetryAfter!.Value, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(121));
    }

    [Fact]
    public async Task AbsoluteLocationIsKept()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.Accepted);
            response.Headers.Location = new Uri("https://status.test/jobs/1");
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Post("/jobs").Send();

        Assert.Equal("https://status.test/jobs/1", res.Location!.AbsoluteUri);
    }

    [Fact]
    public async Task HeaderJoinsMultipleValues()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.OK);
            response.Headers.Add("X-Multi", new[] { "a", "b" });
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();

        Assert.Equal("a, b", res.Header("X-Multi"));
    }
}
