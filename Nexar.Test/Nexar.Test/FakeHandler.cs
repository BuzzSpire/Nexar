using System.Net;
using System.Text;

namespace Nexar.Test;

/// <summary>
/// Records every request and answers with a configurable response. No network involved.
/// </summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public FakeHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "", string mediaType = "application/json")
        : this(_ => Respond(status, body, mediaType))
    {
    }

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest Last => Requests[^1];

    public static HttpResponseMessage Respond(HttpStatusCode status, string body = "", string mediaType = "application/json")
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The request is disposed after sending, so capture everything up front.
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(),
            body)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        });

        var response = await _respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Url,
    Dictionary<string, string> Headers,
    Dictionary<string, string> ContentHeaders,
    string? Body)
{
    public Version Version { get; init; } = new(1, 1);

    public HttpVersionPolicy VersionPolicy { get; init; }
}

public static class TestClient
{
    public static NexarClient Create(FakeHandler handler, Action<ClientBuilder>? configure = null)
    {
        var builder = NexarClient.Builder()
            .BaseUrl("https://api.test")
            .HttpMessageHandler(handler);
        configure?.Invoke(builder);
        return builder.Build();
    }
}
