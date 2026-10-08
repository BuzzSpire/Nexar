using System.Net;

namespace Nexar.Test;

public class ClientTests
{
    [Fact]
    public async Task RelativeUrlWithoutBaseUrlIsBuilderError()
    {
        using var client = NexarClient.Builder().HttpMessageHandler(new FakeHandler()).Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/users").Send());

        Assert.Equal(ErrorKind.Builder, ex.Kind);
    }

    [Fact]
    public void InvalidBaseUrlIsBuilderError()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder().BaseUrl("not a url").Build());

        Assert.Equal(ErrorKind.Builder, ex.Kind);
    }

    [Fact]
    public void HttpClientCannotBeCombinedWithHandlers()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .HttpClient(new HttpClient())
            .HttpMessageHandler(new FakeHandler())
            .Build());

        Assert.Equal(ErrorKind.Builder, ex.Kind);
    }

    [Fact]
    public async Task SerializationFailureIsDeferredToSend()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler);
        var builder = client.Post("/").Form(new { Nested = new { A = 1 } });

        var ex = await Assert.ThrowsAsync<NexarException>(() => builder.Send());

        Assert.Equal(ErrorKind.Builder, ex.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidContentTypeIsDeferredToSend()
    {
        using var client = TestClient.Create(new FakeHandler());
        var builder = client.Post("/").Body("x", "not a media type");

        var ex = await Assert.ThrowsAsync<NexarException>(() => builder.Send());

        Assert.Equal(ErrorKind.Builder, ex.Kind);
    }

    [Fact]
    public async Task ConnectionFailureIsConnectError()
    {
        var handler = new FakeHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.Equal(ErrorKind.Connect, ex.Kind);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task OtherTransportFailureIsRequestError()
    {
        var handler = new FakeHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "garbage"));
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.Equal(ErrorKind.Request, ex.Kind);
    }

    [Fact]
    public async Task ClientTimeoutIsTimeoutError()
    {
        using var client = TestClient.Create(Hanging(), b => b.Timeout(TimeSpan.FromMilliseconds(50)));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsTimeout);
    }

    [Fact]
    public async Task RequestTimeoutOverridesClientTimeout()
    {
        using var client = TestClient.Create(Hanging(), b => b.Timeout(TimeSpan.FromHours(1)));

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            client.Get("/").Timeout(TimeSpan.FromMilliseconds(50)).Send());

        Assert.True(ex.IsTimeout);
    }

    [Fact]
    public async Task CallerCancellationIsNotWrapped()
    {
        using var client = TestClient.Create(Hanging());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Get("/").Send(cts.Token));
    }

    [Fact]
    public async Task HandlersRunInOrder()
    {
        var handler = new FakeHandler();
        using var client = TestClient.Create(handler, b => b
            .AddHandler(new AppendHeaderHandler("first"))
            .AddHandler(new AppendHeaderHandler("second")));

        await client.Get("/").Send();

        Assert.Equal("first, second", handler.Last.Headers["X-Trace"]);
    }

    [Fact]
    public async Task ExternalHttpClientIsNotDisposed()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler);

        using (var client = NexarClient.Builder().BaseUrl("https://api.test").HttpClient(httpClient).Build())
        {
            await client.Get("/").Send();
        }

        using var response = await httpClient.GetAsync("https://api.test/again");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void DefaultConstructorWorks()
    {
        using var client = new NexarClient();

        Assert.Null(client.BaseUrl);
    }

    private static FakeHandler Hanging() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    });

    private sealed class AppendHeaderHandler(string value) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("X-Trace", value);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
