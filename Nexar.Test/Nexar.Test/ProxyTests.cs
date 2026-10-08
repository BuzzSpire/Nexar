using System.Net;
using System.Text;

namespace Nexar.Test;

public class ProxyTests
{
    /// <summary>An HTTP proxy that answers every absolute-form request itself.</summary>
    private static TestServer ProxyServer(string? requiredCredentials = null) => TestServer.Start(request =>
    {
        if (requiredCredentials != null && request.Header("Proxy-Authorization") != $"Basic {requiredCredentials}")
        {
            return new ServerResponse
            {
                Status = 407,
                Reason = "Proxy Authentication Required",
                Headers = [("Proxy-Authenticate", "Basic realm=\"proxy\"")]
            };
        }
        return ServerResponse.Text($"proxied {request.Method} {request.Target}");
    });

    [Fact]
    public async Task RequestsGoThroughTheProxy()
    {
        await using var proxy = ProxyServer();
        using var client = NexarClient.Builder().Proxy(proxy.Url).Build();

        var text = await client.Get("http://api.example.test/users?id=1").Send().ErrorForStatus().Text();

        Assert.Equal("proxied GET http://api.example.test/users?id=1", text);
    }

    [Fact]
    public async Task ProxyCredentialsAnswerA407Challenge()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        await using var proxy = ProxyServer(requiredCredentials: expected);
        using var client = NexarClient.Builder().Proxy(proxy.Url, new NetworkCredential("user", "pass")).Build();

        using var res = await client.Get("http://api.example.test/").Send();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(proxy.Requests, r => r.Header("Proxy-Authorization") == $"Basic {expected}");
    }

    [Fact]
    public async Task BypassedHostsConnectDirectly()
    {
        await using var proxy = ProxyServer();
        await using var target = TestServer.Start(_ => ServerResponse.Text("direct"));
        using var client = NexarClient.Builder()
            .Proxy(proxy.Url)
            .ProxyBypass("127.0.0.*", "*.internal.test")
            .Build();

        var text = await client.Get($"{target.Url}/").Send().Text();

        Assert.Equal("direct", text);
        Assert.Empty(proxy.Requests);
    }

    [Fact]
    public async Task NoProxyOverridesAnEarlierProxy()
    {
        await using var proxy = ProxyServer();
        await using var target = TestServer.Start(_ => ServerResponse.Text("direct"));
        using var client = NexarClient.Builder().Proxy(proxy.Url).NoProxy().Build();

        var text = await client.Get($"{target.Url}/").Send().Text();

        Assert.Equal("direct", text);
        Assert.Empty(proxy.Requests);
    }

    [Theory]
    [InlineData("proxy.local:8080")]
    [InlineData("ftp://proxy.local")]
    [InlineData("not a url")]
    public void InvalidProxyUrlIsRejected(string url)
    {
        Assert.Throws<ArgumentException>(() => NexarClient.Builder().Proxy(url));
    }

    [Theory]
    [InlineData("http://proxy.local:8080")]
    [InlineData("socks5://127.0.0.1:1080")]
    public void ProxyReachesTheHandler(string url)
    {
        using var handler = NexarClient.Builder().Proxy(url).CreateDefaultHandler();

        Assert.True(handler.UseProxy);
        Assert.Equal(new Uri(url), ((WebProxy)handler.Proxy!).Address);
    }
}
