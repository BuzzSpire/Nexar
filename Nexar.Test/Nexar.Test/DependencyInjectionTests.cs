using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nexar.Extensions.DependencyInjection;
using Nexar.Testing;

namespace Nexar.Test;

public class DependencyInjectionTests
{
    private sealed class CorrelationHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Correlation-Id", "c-1");
            return base.SendAsync(request, cancellationToken);
        }
    }

    public sealed class GitHubClient(NexarClient http, TokenSource tokens)
    {
        public Task<string> GetZen() => http.Get("/zen").BearerAuth(tokens.Token).Send().ErrorForStatus().Text();
    }

    public sealed class TokenSource
    {
        public string Token { get; init; } = "ghp_test";
    }

    [Fact]
    public async Task NamedClientUsesConfigurationAndFactoryHandlers()
    {
        var mock = new MockHttp();
        mock.OnGet("/repos").Respond(HttpStatusCode.OK, "[]");
        var services = new ServiceCollection();
        services.AddTransient<CorrelationHandler>();
        services.AddNexarClient("github", b => b.BaseUrl("https://api.github.test").UserAgent("my-app"))
            .AddHttpMessageHandler<CorrelationHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => mock);
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<INexarClientFactory>().CreateClient("github");
        await client.Get("/repos").Send().ErrorForStatus();

        var request = Assert.Single(mock.Requests);
        Assert.Equal("https://api.github.test/repos", request.Url.AbsoluteUri);
        Assert.Equal("my-app", request.Header("User-Agent"));
        Assert.Equal("c-1", request.Header("X-Correlation-Id"));
    }

    [Fact]
    public async Task TypedClientGetsNexarClientAndOtherServices()
    {
        var mock = new MockHttp();
        mock.OnGet("/zen").WithHeader("Authorization", "Bearer ghp_test").Respond(HttpStatusCode.OK, "Keep it logically awesome.");
        var services = new ServiceCollection();
        services.AddSingleton(new TokenSource());
        services.AddNexarClient<GitHubClient>(b => b.BaseUrl("https://api.github.test"))
            .ConfigurePrimaryHttpMessageHandler(() => mock);
        await using var provider = services.BuildServiceProvider();

        var zen = await provider.GetRequiredService<GitHubClient>().GetZen();

        Assert.Equal("Keep it logically awesome.", zen);
    }

    [Fact]
    public async Task ConfigurationCanUseServices()
    {
        var mock = new MockHttp();
        mock.OnAny().Respond();
        var services = new ServiceCollection();
        services.AddSingleton(new TokenSource { Token = "from-di" });
        services.AddNexarClient("api", (sp, b) => b
                .BaseUrl("https://api.test")
                .Auth(Auth.Bearer(sp.GetRequiredService<TokenSource>().Token)))
            .ConfigurePrimaryHttpMessageHandler(() => mock);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<INexarClientFactory>().CreateClient("api").Get("/").Send();

        Assert.Equal("Bearer from-di", mock.Requests[0].Header("Authorization"));
    }

    [Fact]
    public async Task NexarTimeoutIsNotCappedByTheFactoryDefault()
    {
        var mock = new MockHttp();
        mock.OnAny().Delay(TimeSpan.FromMilliseconds(200)).Respond();
        var services = new ServiceCollection();
        services.AddNexarClient("api", b => b.BaseUrl("https://api.test").Timeout(TimeSpan.FromMilliseconds(50)))
            .ConfigurePrimaryHttpMessageHandler(() => mock);
        await using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<NexarException>(() =>
            provider.GetRequiredService<INexarClientFactory>().CreateClient("api").Get("/").Send());

        Assert.True(ex.IsTimeout);
    }

    [Fact]
    public async Task HandlerSettingsMustGoOnTheHttpClientBuilder()
    {
        var services = new ServiceCollection();
        services.AddNexarClient("api", b => b.BaseUrl("https://api.test").CookieStore());
        await using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<NexarException>(() => provider.GetRequiredService<INexarClientFactory>().CreateClient("api"));

        Assert.True(ex.IsBuilder);
        Assert.Contains("CookieStore()", ex.Message);
    }
}
