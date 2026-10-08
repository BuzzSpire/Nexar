using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Nexar.Extensions.DependencyInjection;

/// <summary>
/// Creates the <see cref="NexarClient"/>s registered with <c>AddNexarClient</c>.
/// </summary>
public interface INexarClientFactory
{
    /// <summary>
    /// Creates a client for <paramref name="name"/>, backed by <c>IHttpClientFactory</c>'s client of the same name.
    /// Clients are cheap; create one per use or per scope, there is no need to dispose them.
    /// </summary>
    NexarClient CreateClient(string name);
}

/// <summary>
/// Registers Nexar clients on top of <c>IHttpClientFactory</c>.
/// </summary>
public static class NexarServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named Nexar client, resolved with <see cref="INexarClientFactory.CreateClient"/>.
    /// Configure handlers (resilience, logging, primary handler) on the returned <see cref="IHttpClientBuilder"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddNexarClient("github", b => b.BaseUrl("https://api.github.com").UserAgent("my-app"))
    ///     .AddHttpMessageHandler&lt;CorrelationHandler&gt;();
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddNexarClient(this IServiceCollection services, string name, Action<ClientBuilder>? configure = null) =>
        services.AddNexarClient(name, (_, builder) => configure?.Invoke(builder));

    /// <summary>
    /// Registers a named Nexar client whose configuration uses other services, e.g. options or a token cache.
    /// </summary>
    public static IHttpClientBuilder AddNexarClient(this IServiceCollection services, string name, Action<IServiceProvider, ClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton<INexarClientFactory, DefaultNexarClientFactory>();
        services.Configure<NexarClientOptions>(name, options => options.Configure.Add(configure));
        return services.AddHttpClient(name);
    }

    /// <summary>
    /// Registers <typeparamref name="TClient"/> as a typed client: it is created per resolution with a
    /// <see cref="NexarClient"/> in its constructor.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddNexarClient&lt;GitHubClient&gt;(b => b.BaseUrl("https://api.github.com"));
    ///
    /// public sealed class GitHubClient(NexarClient http) { ... }
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddNexarClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services, Action<ClientBuilder>? configure = null)
        where TClient : class =>
        services.AddNexarClient<TClient>((_, builder) => configure?.Invoke(builder));

    /// <summary>
    /// Registers a typed client whose configuration uses other services.
    /// </summary>
    public static IHttpClientBuilder AddNexarClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services, Action<IServiceProvider, ClientBuilder> configure)
        where TClient : class
    {
        var name = typeof(TClient).FullName ?? typeof(TClient).Name;
        var builder = services.AddNexarClient(name, configure);
        services.AddTransient(provider =>
            ActivatorUtilities.CreateInstance<TClient>(provider, provider.GetRequiredService<INexarClientFactory>().CreateClient(name)));
        return builder;
    }
}

internal sealed class NexarClientOptions
{
    public List<Action<IServiceProvider, ClientBuilder>> Configure { get; } = new();
}

internal sealed class DefaultNexarClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<NexarClientOptions> options,
    IServiceProvider services) : INexarClientFactory
{
    public NexarClient CreateClient(string name)
    {
        var httpClient = httpClientFactory.CreateClient(name);
        // Nexar enforces its own timeouts; the factory client's 100 s default would otherwise cap them.
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var builder = NexarClient.Builder().HttpClient(httpClient);
        foreach (var configure in options.Get(name).Configure)
        {
            configure(services, builder);
        }
        return builder.Build();
    }
}
