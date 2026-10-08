// Exercises Nexar's trimming/AOT-safe surface without network access.
// Exit code 0 means every check passed in the published native binary.
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Nexar;

using var client = NexarClient.Builder()
    .BaseUrl("https://api.test")
    .HttpMessageHandler(new EchoHandler())
    .Build();

var failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "ok  " : "FAIL")} {name}");
    if (!condition)
    {
        failures++;
    }
}

var created = await client.Post("/users/{id}")
    .Path("id", 42)
    .Query(new Dictionary<string, string> { ["dry"] = "true" })
    .Json(new User(42, "Ada"), AppJsonContext.Default.User)
    .Send()
    .ErrorForStatus()
    .Json(AppJsonContext.Default.User);
Check(created == new User(42, "Ada"), "JSON round trip with JsonTypeInfo");

var form = await client.Post("/form").Form(new Dictionary<string, string> { ["a"] = "1" }).Send().Text();
Check(form == "a=1", "form from key/value pairs");

var users = new List<User>();
await foreach (var user in client.Get("/stream").Send().JsonStream(AppJsonContext.Default.User))
{
    users.Add(user);
}
Check(users.Count == 2, "JsonStream with JsonTypeInfo");

var events = new List<string>();
await foreach (var e in client.Get("/events").Send().Events())
{
    events.Add(e.Data);
}
Check(events.SequenceEqual(["a", "b"]), "Server-Sent Events");

try
{
    await client.Get("/missing").Send().ErrorForStatus();
    Check(false, "ErrorForStatus throws");
}
catch (NexarException e) when (e.IsStatus)
{
    Check(e.Problem?.Title == "Not Found", "problem details");
}

return failures == 0 ? 0 : 1;

record User(int Id, string Name);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(User))]
partial class AppJsonContext : JsonSerializerContext;

/// <summary>Answers from memory so the smoke test needs no network.</summary>
sealed class EchoHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return path switch
        {
            "/users/42" => Respond(HttpStatusCode.Created, body, "application/json"),
            "/form" => Respond(HttpStatusCode.OK, body, "text/plain"),
            "/stream" => Respond(HttpStatusCode.OK, "[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]", "application/json"),
            "/events" => Respond(HttpStatusCode.OK, "data: a\n\ndata: b\n\n", "text/event-stream"),
            _ => Respond(HttpStatusCode.NotFound, "{\"title\":\"Not Found\",\"status\":404}", "application/problem+json")
        };
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
