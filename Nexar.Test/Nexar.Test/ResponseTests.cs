using System.Net;

namespace Nexar.Test;

public class ResponseTests
{
    private record User(int Id, string Name);

    [Fact]
    public async Task ExposesStatusAndHeaders()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.Created, "{}");
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return response;
        });
        using var client = TestClient.Create(handler);

        using var res = await client.Post("/users").Send();

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(201, res.Status);
        Assert.True(res.IsSuccess);
        Assert.Equal("\"v1\"", res.Headers.ETag!.Tag);
        Assert.Equal("application/json", res.ContentHeaders.ContentType!.MediaType);
        Assert.Equal("https://api.test/users", res.Url.ToString());
    }

    [Fact]
    public async Task ReadsJsonCaseInsensitivelyByDefault()
    {
        var handler = new FakeHandler(body: "{\"ID\":1,\"name\":\"Ada\"}");
        using var client = TestClient.Create(handler);

        var user = await client.Get("/users/1").Send().Json<User>();

        Assert.Equal(new User(1, "Ada"), user);
    }

    [Fact]
    public async Task BodyCanBeReadMoreThanOnce()
    {
        var handler = new FakeHandler(body: "{\"id\":1,\"name\":\"Ada\"}");
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/users/1").Send();

        Assert.Equal("{\"id\":1,\"name\":\"Ada\"}", await res.Text());
        Assert.Equal("Ada", (await res.Json<User>()).Name);
        Assert.Equal("{\"id\":1,\"name\":\"Ada\"}"u8.ToArray(), await res.Bytes());
    }

    [Fact]
    public async Task ReadsTextAndBytesThroughTaskChain()
    {
        var handler = new FakeHandler(body: "hello", mediaType: "text/plain");
        using var client = TestClient.Create(handler);

        Assert.Equal("hello", await client.Get("/").Send().Text());
        Assert.Equal("hello"u8.ToArray(), await client.Get("/").Send().Bytes());
    }

    [Fact]
    public async Task StreamsTheBody()
    {
        var handler = new FakeHandler(body: "streamed");
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();
        await using var stream = await res.Stream();
        using var reader = new StreamReader(stream);

        Assert.Equal("streamed", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ErrorForStatusPassesSuccessAndRedirects()
    {
        var handler = new FakeHandler(HttpStatusCode.NotModified);
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send().ErrorForStatus();

        Assert.Equal(HttpStatusCode.NotModified, res.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorForStatusThrowsOnClientAndServerErrors(HttpStatusCode status)
    {
        var handler = new FakeHandler(status);
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/missing").Send().ErrorForStatus());

        Assert.Equal(ErrorKind.Status, ex.Kind);
        Assert.True(ex.IsStatus);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal("https://api.test/missing", ex.Url!.ToString());
    }

    [Fact]
    public async Task ErrorStatusesAreNotExceptionsWithoutErrorForStatus()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}");
        using var client = TestClient.Create(handler);

        using var res = await client.Get("/").Send();

        Assert.False(res.IsSuccess);
        Assert.Equal("{\"error\":\"bad\"}", await res.Text());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task JsonThrowsDecodeErrorForInvalidBodies(string body)
    {
        var handler = new FakeHandler(body: body);
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send().Json<User>());

        Assert.Equal(ErrorKind.Decode, ex.Kind);
    }

    [Fact]
    public async Task JsonNullIsAllowedForNullableValueTypes()
    {
        var handler = new FakeHandler(body: "null");
        using var client = TestClient.Create(handler);

        Assert.Null(await client.Get("/").Send().Json<int?>());
    }
}
