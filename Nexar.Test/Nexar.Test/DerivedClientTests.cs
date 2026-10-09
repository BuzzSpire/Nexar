using System.Net;
using Nexar.Testing;

namespace Nexar.Test;

public class DerivedClientTests
{
    [Fact]
    public async Task DefaultQueryIsAddedToEveryRequest()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient(configure: b => b.DefaultQuery("api-version", "2024-05-01").DefaultQuery("format", "json"));

        await client.Get("/a").Send();
        await client.Get("/b").Query("page", 2).Send();
        await client.Get("/c").Query("format", "xml").Send();

        Assert.Equal("?api-version=2024-05-01&format=json", mock.Requests[0].Url.Query);
        Assert.Equal("?api-version=2024-05-01&format=json&page=2", mock.Requests[1].Url.Query);
        Assert.Equal("?api-version=2024-05-01&format=xml", mock.Requests[2].Url.Query);   // request value replaces the default
    }

    [Fact]
    public async Task DefaultQueryReplacesAnEarlierValue()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient(configure: b => b.DefaultQuery("v", 1).DefaultQuery("v", 2));

        await client.Get("/").Send();

        Assert.Equal("?v=2", mock.Requests[0].Url.Query);
    }

    [Fact]
    public async Task DerivedClientChangesRequestDefaults()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        using var client = mock.CreateClient("https://api.test", b => b.DefaultHeader("X-App", "nexar").DefaultQuery("api-version", "1"));

        using var tenant = client.With(b => b
            .BaseUrl("https://api.test/tenants/acme")
            .DefaultHeader("X-Tenant", "acme")
            .Auth(Auth.Bearer("acme-token")));

        await tenant.Get("/orders").Send();
        await client.Get("/orders").Send();

        var tenantRequest = mock.Requests[0];
        Assert.Equal("https://api.test/tenants/acme/orders?api-version=1", tenantRequest.Url.AbsoluteUri);
        Assert.Equal("nexar", tenantRequest.Header("X-App"));             // inherited
        Assert.Equal("acme", tenantRequest.Header("X-Tenant"));
        Assert.Equal("Bearer acme-token", tenantRequest.Header("Authorization"));

        var parentRequest = mock.Requests[1];
        Assert.Equal("https://api.test/orders?api-version=1", parentRequest.Url.AbsoluteUri);
        Assert.Null(parentRequest.Header("X-Tenant"));                     // the parent is unchanged
        Assert.Null(parentRequest.Header("Authorization"));
    }

    [Fact]
    public async Task DerivedClientInheritsRetriesAndCanChangeThem()
    {
        var mock = new MockHttp();
        mock.OnGet("/").Respond(HttpStatusCode.ServiceUnavailable);
        using var client = mock.CreateClient(configure: b => b.Retry(2, TimeSpan.Zero));
        using var noRetries = client.With(b => b.Retry(0));
        using var inherited = client.With(_ => { });

        using (await inherited.Get("/").Send()) { }
        var afterInherited = mock.Requests.Count;
        using (await noRetries.Get("/").Send()) { }

        Assert.Equal(3, afterInherited);
        Assert.Equal(4, mock.Requests.Count);
    }

    [Fact]
    public async Task DisposingADerivedClientKeepsThePoolOpen()
    {
        await using var server = TestServer.Start(_ => ServerResponse.Text("ok"));
        using var client = NexarClient.Builder().BaseUrl(server.Url).Build();

        using (var derived = client.With(b => b.DefaultHeader("X-Derived", "1")))
        {
            Assert.Equal("ok", await derived.Get("/").Send().Text());
        }

        Assert.Equal("ok", await client.Get("/").Send().Text());
        Assert.Equal("1", server.Requests.First().Header("X-Derived"));
    }

    [Fact]
    public void HandlerSettingsAreRejectedOnDerivedClients()
    {
        using var client = NexarClient.Builder().Build();

        var ex = Assert.Throws<NexarException>(() => client.With(b => b.CookieStore()));

        Assert.True(ex.IsBuilder);
        Assert.Contains("CookieStore()", ex.Message);
    }

    [Fact]
    public async Task DerivedClientKeepsCustomRedaction()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        var logger = new ListLogger();
        using var client = mock.CreateClient(configure: b => b.Logger(logger).RedactQueryParameters("ticket"));
        using var derived = client.With(b => b.DefaultQuery("ticket", "secret-ticket"));

        await derived.Get("/").Send();

        Assert.DoesNotContain(logger.Messages, m => m.Contains("secret-ticket"));
        Assert.Contains(logger.Messages, m => m.Contains("ticket=REDACTED"));
    }

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
