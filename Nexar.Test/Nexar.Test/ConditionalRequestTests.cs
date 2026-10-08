using System.Net;
using System.Net.Http.Headers;

namespace Nexar.Test;

public class ConditionalRequestTests
{
    /// <summary>A resource with ETag "v1", modified at a fixed date, that honors If-None-Match and If-Match.</summary>
    private static FakeHandler Resource() => new(request =>
    {
        var etag = new EntityTagHeaderValue("\"v1\"");
        if (request.Headers.IfNoneMatch.Any(t => t.Tag == etag.Tag || t.Tag == "*"))
        {
            return new HttpResponseMessage(HttpStatusCode.NotModified) { Headers = { ETag = etag } };
        }
        if (request.Headers.IfMatch.Count > 0 && !request.Headers.IfMatch.Any(t => t.Tag == etag.Tag && !t.IsWeak))
        {
            return FakeHandler.Respond(HttpStatusCode.PreconditionFailed);
        }
        var response = FakeHandler.Respond(HttpStatusCode.OK, "{\"v\":1}");
        response.Headers.ETag = etag;
        return response;
    });

    [Fact]
    public async Task RevalidationWithETagReturnsNotModified()
    {
        var handler = Resource();
        using var client = TestClient.Create(handler);

        using var first = await client.Get("/doc").Send();
        using var second = await client.Get("/doc").IfNoneMatch(first.ETag!).Send().ErrorForStatus();

        Assert.False(first.IsNotModified);
        Assert.True(second.IsNotModified);
        Assert.Equal("\"v1\"", handler.Last.Headers["If-None-Match"]);
    }

    [Theory]
    [InlineData("v1", "\"v1\"")]
    [InlineData("\"v1\"", "\"v1\"")]
    [InlineData("W/\"v1\"", "W/\"v1\"")]
    [InlineData("*", "*")]
    public async Task ETagsAreQuotedCorrectly(string input, string expected)
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").IfNoneMatch(input).Send();

        Assert.Equal(expected, handler.Last.Headers["If-None-Match"]);
    }

    [Fact]
    public async Task MultipleETags()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        await client.Get("/").IfNoneMatch("a", "W/\"b\"").Send();

        Assert.Equal("\"a\", W/\"b\"", handler.Last.Headers["If-None-Match"]);
    }

    [Fact]
    public async Task StaleIfMatchFailsWith412()
    {
        using var client = TestClient.Create(Resource());

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Put("/doc").IfMatch("v0").Json(new { v = 2 }).Send().ErrorForStatus());

        Assert.Equal(HttpStatusCode.PreconditionFailed, ex.StatusCode);
    }

    [Fact]
    public async Task CurrentIfMatchSucceeds()
    {
        using var client = TestClient.Create(Resource());

        using var res = await client.Put("/doc").IfMatch(new EntityTagHeaderValue("\"v1\"")).Json(new { v = 2 }).Send().ErrorForStatus();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task DatesUseImfFixdate()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);
        var date = new DateTimeOffset(2026, 10, 8, 15, 30, 0, TimeSpan.FromHours(3));

        await client.Get("/").IfModifiedSince(date).IfUnmodifiedSince(date).Send();

        Assert.Equal("Thu, 08 Oct 2026 12:30:00 GMT", handler.Last.Headers["If-Modified-Since"]);
        Assert.Equal("Thu, 08 Oct 2026 12:30:00 GMT", handler.Last.Headers["If-Unmodified-Since"]);
    }

    [Fact]
    public async Task InvalidETagIsBuilderError()
    {
        using var client = TestClient.Create(new FakeHandler());

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").IfNoneMatch("\"unterminated").Send());

        Assert.True(ex.IsBuilder);
    }

    [Fact]
    public async Task NotModifiedIsNotAnError()
    {
        using var client = TestClient.Create(new FakeHandler(HttpStatusCode.NotModified));

        using var res = await client.Get("/").Send().ErrorForStatus();

        Assert.True(res.IsNotModified);
    }
}
