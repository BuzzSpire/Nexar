using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nexar.Test;

public class BuildExecuteTests
{
    [Fact]
    public async Task BuildThenExecute()
    {
        var handler = new FakeHandler(HttpStatusCode.Created);
        using var client = TestClient.Create(handler, b => b.DefaultHeader("X-Default", "d"));

        var request = client.Post("/orders/{id}").Path("id", 7).Query("dry", true).Json(new { qty = 2 }).Build();
        using var res = await client.Execute(request);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/orders/7?dry=true", request.Url.AbsoluteUri);
        Assert.Equal("d", request.Header("X-Default"));
        Assert.True(request.HasBody);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("{\"qty\":2}", handler.Last.Body);
    }

    [Fact]
    public void BuildRaisesBuilderErrorsWithoutNetwork()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var ex = Assert.Throws<NexarException>(() => client.Get("/").Header("X", "a\r\nb").Build());

        Assert.True(ex.IsBuilder);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SignTheBodyAndHeaders()
    {
        var key = "secret"u8.ToArray();
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var request = client.Put("/doc").Body("payload").Build();
        var body = await request.ReadBodyAsync();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{request.Method} {request.Url.PathAndQuery}\n").Concat(body!).ToArray()));
        request.SetHeader("X-Signature", signature);
        await client.Execute(request).ErrorForStatus();

        Assert.Equal(signature, handler.Last.Headers["X-Signature"]);
        Assert.Equal("payload", handler.Last.Body);
    }

    [Fact]
    public async Task HeadersCanBeRemovedAndListed()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);

        var request = client.Get("/").Header("X-A", "1").HeaderAppend("X-B", "2").HeaderAppend("X-B", "3").Build();
        request.RemoveHeader("X-A");
        await client.Execute(request);

        Assert.Equal(new[] { "2", "3" }, request.Headers["X-B"]);
        Assert.False(handler.Last.Headers.ContainsKey("X-A"));
        Assert.Equal("2, 3", handler.Last.Headers["X-B"]);
    }

    [Fact]
    public void SetHeaderValidates()
    {
        using var client = TestClient.Create(new FakeHandler());
        var request = client.Get("/").Build();

        Assert.Throws<ArgumentException>(() => request.SetHeader("X", "a\r\nb"));
    }

    [Fact]
    public async Task ExecuteAppliesAuthAndRetries()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK });
        var handler = new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue()));
        using var client = TestClient.Create(handler, b => b.Retry(1, TimeSpan.Zero).Auth(Auth.Bearer("t")));

        using var res = await client.Execute(client.Get("/").Build());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer t", r.Headers["Authorization"]));
    }

    [Fact]
    public async Task ReadBodyOfStreamBodyThrows()
    {
        using var client = TestClient.Create(new FakeHandler());
        var request = client.Put("/").Body(new MemoryStream(new byte[3])).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => request.ReadBodyAsync());
        Assert.Null(request.ContentType);
    }

    [Fact]
    public async Task RequestWithoutBody()
    {
        using var client = TestClient.Create(new FakeHandler());
        var request = client.Get("/").Build();

        Assert.False(request.HasBody);
        Assert.Null(await request.ReadBodyAsync());
    }

    [Fact]
    public async Task TryCloneCopiesEverything()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);
        var original = client.Post("/items/{id}").Path("id", 1).Query("a", 1).Header("X-Trace", "x").Json(new { v = 1 });

        var clone = original.TryClone()!;
        clone.Query("b", 2).HeaderAppend("X-Trace", "y");
        await original.Send();
        await clone.Send();

        Assert.Equal("https://api.test/items/1?a=1", handler.Requests[0].Url.AbsoluteUri);
        Assert.Equal("x", handler.Requests[0].Headers["X-Trace"]);
        Assert.Equal("https://api.test/items/1?a=1&b=2", handler.Requests[1].Url.AbsoluteUri);
        Assert.Equal("x, y", handler.Requests[1].Headers["X-Trace"]);
        Assert.All(handler.Requests, r => Assert.Equal("{\"v\":1}", r.Body));
    }

    [Fact]
    public void TryCloneOfStreamBodyIsNull()
    {
        using var client = TestClient.Create(new FakeHandler());

        Assert.Null(client.Put("/").Body(new MemoryStream()).TryClone());
    }
}
