using System.Net;

namespace Nexar.Test;

public class RedirectTests
{
    /// <summary>/hop/{n} redirects to /hop/{n-1}; /hop/0 answers "done".</summary>
    private static TestServer HopServer() => TestServer.Start(request =>
    {
        var n = int.Parse(request.Target.Split('/').Last());
        return n == 0 ? ServerResponse.Text("done") : ServerResponse.Redirect($"/hop/{n - 1}");
    });

    [Fact]
    public async Task FollowsRedirectsByDefault()
    {
        await using var server = HopServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        using var res = await client.Get("/hop/3").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("done", await res.Text());
        Assert.EndsWith("/hop/0", res.Url.AbsolutePath);
    }

    [Fact]
    public async Task TooManyRedirectsIsRedirectError()
    {
        await using var server = HopServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/hop/11").Send());

        Assert.True(ex.IsRedirect);
        Assert.Equal(11, server.Requests.Count);   // the original request + 10 followed hops
    }

    [Fact]
    public async Task LimitedPolicy()
    {
        await using var server = HopServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Redirects(RedirectPolicy.Limited(2)).Build();

        using var ok = await client.Get("/hop/2").Send();
        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/hop/3").Send());

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ex.IsRedirect);
    }

    [Fact]
    public async Task NonePolicyReturnsTheRedirect()
    {
        await using var server = HopServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Redirects(RedirectPolicy.None).Build();

        using var res = await client.Get("/hop/1").Send();

        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Equal($"{server.Url}/hop/0", res.Location!.AbsoluteUri);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task HttpsToHttpIsNotFollowed()
    {
        using var certificate = TestCertificates.CreateServerCertificate();
        await using var server = TestServer.StartTls(certificate, _ => ServerResponse.Redirect("http://127.0.0.1:1/insecure"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).DangerAcceptInvalidCerts().Build();

        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/secure").Send());

        Assert.True(ex.IsRedirect);
    }

    [Fact]
    public void RedirectsCannotBeCombinedWithCustomHandler()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .HttpMessageHandler(new FakeHandler())
            .Redirects(RedirectPolicy.None)
            .Build());

        Assert.True(ex.IsBuilder);
        Assert.Contains("Redirects()", ex.Message);
    }

    [Fact]
    public void LimitedRejectsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedirectPolicy.Limited(-1));
        Assert.Same(RedirectPolicy.None, RedirectPolicy.Limited(0));
    }
}
