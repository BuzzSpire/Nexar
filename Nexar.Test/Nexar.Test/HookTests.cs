using System.Net;
using Nexar.Testing;

namespace Nexar.Test;

public class HookTests
{
    [Fact]
    public async Task OnRequestAddsHeadersAfterAuthentication()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        string? authorizationSeenByHook = null;
        using var client = mock.CreateClient(configure: b => b
            .Auth(Auth.Bearer("t"))
            .OnRequest((request, _) =>
            {
                authorizationSeenByHook = request.Headers.Authorization?.ToString();
                request.Headers.Add("X-Client-Time", "now");
                return ValueTask.CompletedTask;
            }));

        await client.Get("/").Send();

        Assert.Equal("Bearer t", authorizationSeenByHook);
        Assert.Equal("now", mock.Requests[0].Header("X-Client-Time"));
    }

    [Fact]
    public async Task HooksRunForEveryAttemptInOrder()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK);
        var calls = new List<string>();
        using var client = mock.CreateClient(configure: b => b
            .Retry(1, TimeSpan.Zero)
            .OnRequest((_, _) => { calls.Add("request-1"); return ValueTask.CompletedTask; })
            .OnRequest((_, _) => { calls.Add("request-2"); return ValueTask.CompletedTask; })
            .OnResponse((response, _) => { calls.Add($"response-{(int)response.StatusCode}"); return ValueTask.CompletedTask; }));

        using var res = await client.Get("/").Send();

        Assert.Equal(new[] { "request-1", "request-2", "response-503", "request-1", "request-2", "response-200" }, calls);
    }

    [Fact]
    public async Task HooksWorkWithCustomHandlersAndDerivedClients()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        var statuses = new List<HttpStatusCode>();
        using var client = mock.CreateClient(configure: b => b.OnResponse((r, _) => { statuses.Add(r.StatusCode); return ValueTask.CompletedTask; }));
        using var derived = client.With(b => b.DefaultHeader("X", "1"));

        await derived.Get("/").Send();

        Assert.Equal(new[] { HttpStatusCode.OK }, statuses);
    }

    [Fact]
    public async Task FailingHookIsRequestError()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient(configure: b => b.OnRequest((_, _) => throw new InvalidOperationException("broken hook")));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.Equal(ErrorKind.Request, ex.Kind);
        Assert.Contains("broken hook", ex.Message);
        Assert.Empty(mock.Requests);
    }

    [Fact]
    public async Task FailingResponseHookIsRequestError()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient(configure: b => b.OnResponse((_, _) => throw new InvalidOperationException("bad")));

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.Equal(ErrorKind.Request, ex.Kind);
    }
}
