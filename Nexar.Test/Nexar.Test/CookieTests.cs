using System.Net;

namespace Nexar.Test;

public class CookieTests
{
    /// <summary>/login sets a session cookie; any other path echoes the Cookie header.</summary>
    private static TestServer CookieServer() => TestServer.Start(request => request.Target == "/login"
        ? ServerResponse.Text("ok", headers: ("Set-Cookie", "session=abc123; Path=/"))
        : ServerResponse.Text(request.Header("Cookie") ?? "(none)"));

    [Fact]
    public async Task ClientIsStatelessByDefault()
    {
        await using var server = CookieServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        await client.Get("/login").Send().Text();
        var cookie = await client.Get("/me").Send().Text();

        Assert.Equal("(none)", cookie);
    }

    [Fact]
    public async Task CookieStoreSendsCookiesBack()
    {
        await using var server = CookieServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).CookieStore().Build();

        await client.Get("/login").Send().Text();
        var cookie = await client.Get("/me").Send().Text();

        Assert.Equal("session=abc123", cookie);
    }

    [Fact]
    public async Task SuppliedJarCanBeInspectedAndPreFilled()
    {
        await using var server = CookieServer();
        var jar = new CookieContainer();
        jar.Add(new Uri(server.Url), new Cookie("theme", "dark"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).CookieStore(jar).Build();

        var before = await client.Get("/me").Send().Text();
        await client.Get("/login").Send().Text();

        Assert.Equal("theme=dark", before);
        Assert.Equal("abc123", jar.GetCookies(new Uri(server.Url))["session"]!.Value);
    }

    [Fact]
    public async Task ManualCookieHeaderIsSentWithoutStore()
    {
        await using var server = CookieServer();
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        var cookie = await client.Get("/me").Header("Cookie", "manual=1").Send().Text();

        Assert.Equal("manual=1", cookie);
    }

    [Fact]
    public void CookieStoreCannotBeCombinedWithHttpClient()
    {
        var ex = Assert.Throws<NexarException>(() => NexarClient.Builder()
            .HttpClient(new HttpClient())
            .CookieStore()
            .Build());

        Assert.Contains("CookieStore()", ex.Message);
    }
}
