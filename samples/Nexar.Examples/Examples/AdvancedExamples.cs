using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nexar.Extensions.DependencyInjection;
using Nexar.Testing;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using static Nexar.Examples.Example;

namespace Nexar.Examples;

public sealed class UsersApi(NexarClient http)
{
    public Task<User> Get(int id) => http.Get("/users/{id}").Path("id", id).Send().ErrorForStatus().Json<User>();
}

public static class AdvancedExamples
{
    public static async Task Authentication(string baseUrl)
    {
        // snippet:auth-bearer
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

        var login = await client.Post("/login")
            .Form(new Dictionary<string, string> { ["username"] = "ada", ["password"] = "secret" })
            .Send()
            .ErrorForStatus()
            .Json<Dictionary<string, string>>();

        var me = await client.Get("/me").BearerAuth(login["token"]).Send().ErrorForStatus().Json<User>();
        // end-snippet

        // snippet:auth-oauth2
        using var reports = NexarClient.Builder()
            .BaseUrl(baseUrl)
            .Auth(Auth.OAuth2ClientCredentials($"{baseUrl}/oauth/token", "reports-app", "s3cret"))
            .Build();

        // The token is fetched once, cached until shortly before it expires, and refreshed after a 401.
        var daily = await reports.Get("/reports/daily").Send().ErrorForStatus().Text();
        // end-snippet

        Expect(me.Name == "Ada Lovelace", "bearer auth");
        Expect(daily.Contains("1234"), "oauth2 client credentials");
    }

    public static async Task Streaming(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

        // snippet:streaming-sse
        await foreach (var e in client.Get("/events").Send().ErrorForStatus().Events())
        {
            if (e.Event == "done")
            {
                break;
            }
            Console.Write(e.Data);   // Hello from Nexar
        }
        Console.WriteLine();
        // end-snippet

        var text = new System.Text.StringBuilder();
        await foreach (var e in client.Get("/events").Send().Events())
        {
            if (e.Event == "delta")
            {
                text.Append(e.Data);
            }
        }

        // snippet:streaming-ndjson
        await foreach (var entry in client.Get("/logs").Send().JsonLines<LogEntry>())
        {
            Console.WriteLine($"{entry.Level}: {entry.Message}");
        }
        // end-snippet

        // snippet:pagination
        var names = new List<string>();
        await foreach (var repo in client.Get("/repos").Paginate<Repo>())   // follows Link: <...>; rel="next"
        {
            names.Add(repo.Name);
        }
        Console.WriteLine(names.Count);   // 6, from 3 pages
        // end-snippet

        Expect(text.ToString() == "Hello from Nexar", "server-sent events");
        Expect(names.Count == 6, "pagination over three pages");
    }

    public static async Task Files(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();
        var directory = Directory.CreateTempSubdirectory("nexar-examples-").FullName;
        try
        {
            var path = Path.Combine(directory, "report.csv");

            // snippet:files
            // Saved to a temporary file and moved into place only when complete.
            await client.Get("/files/report.csv")
                .DownloadProgress(new Progress<TransferProgress>(p => Console.Write($"\r{p.BytesTransferred} bytes")))
                .Send()
                .ErrorForStatus()
                .SaveTo(path);
            Console.WriteLine();

            // Resumable: after a failure, the next call continues from where it stopped.
            long size = await client.Get("/files/report.csv").DownloadTo(path, resume: true);

            // Just the last 100 bytes
            var tail = await client.Get("/files/report.csv").RangeSuffix(100).Send().Text();
            // end-snippet

            Expect(new FileInfo(path).Length == size && size > 10_000, "file downloaded");
            Expect(tail.Length == 100, "range request");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static async Task Caching(string baseUrl)
    {
        // snippet:caching
        using var client = NexarClient.Builder()
            .BaseUrl(baseUrl)
            .Cache(new MemoryHttpCache())
            .Build();

        using var first = await client.Get("/catalog").Send();               // Cache-Control: max-age=60
        using var second = await client.Get("/catalog").Send();              // served from memory
        using var checkedAgain = await client.Get("/catalog").NoCache().Send(); // If-None-Match -> 304

        Console.WriteLine($"{first.CacheStatus} {second.CacheStatus} {checkedAgain.CacheStatus}");
        // Miss Hit Revalidated
        // end-snippet

        Expect(first.CacheStatus == CacheStatus.Miss && second.CacheStatus == CacheStatus.Hit
            && checkedAgain.CacheStatus == CacheStatus.Revalidated, "cache statuses");
    }

    public static async Task Observability(string baseUrl)
    {
        // snippet:observability
        var spans = new List<Activity>();
        var metrics = new List<Metric>();
        using var tracing = Sdk.CreateTracerProviderBuilder()
            .AddSource("Nexar")
            .AddInMemoryExporter(spans)      // or AddConsoleExporter() / AddOtlpExporter()
            .Build();
        using var meters = Sdk.CreateMeterProviderBuilder()
            .AddMeter("Nexar")
            .AddInMemoryExporter(metrics)
            .Build();

        using var client = NexarClient.Builder().BaseUrl(baseUrl).Retry(3, TimeSpan.FromMilliseconds(50)).Build();
        await client.Get("/flaky").Query("api_key", "secret").Send();
        meters.ForceFlush();

        var span = spans.Single();
        Console.WriteLine(span.GetTagItem("url.full"));                  // .../flaky?api_key=REDACTED
        Console.WriteLine(span.GetTagItem("http.response.status_code")); // 200
        Console.WriteLine(span.GetTagItem("http.request.resend_count")); // 2 (two 503s were retried)
        Console.WriteLine(string.Join(", ", metrics.Select(m => m.Name)));
        // http.client.request.duration, http.client.active_requests, nexar.client.resends
        // end-snippet

        Expect(((string)span.GetTagItem("url.full")!).EndsWith("api_key=REDACTED"), "secret redacted");
        Expect(metrics.Any(m => m.Name == "http.client.request.duration"), "duration metric exported");
    }

    public static async Task DependencyInjection(string baseUrl)
    {
        // snippet:dependency-injection
        var services = new ServiceCollection();
        services.AddNexarClient<UsersApi>(b => b.BaseUrl(baseUrl).Retry(2));

        await using var provider = services.BuildServiceProvider();
        var user = await provider.GetRequiredService<UsersApi>().Get(2);
        Console.WriteLine(user.Name);   // Alan Turing
        // end-snippet

        Expect(user.Name == "Alan Turing", "typed client from DI");
    }

    public static async Task Testing(string _)
    {
        // snippet:testing
        var mock = new MockHttp();
        mock.OnGet("/users/1").RespondJson(new User(1, "Mock User", "mock@example.com"));
        mock.OnGet("/users/2")
            .Respond(HttpStatusCode.ServiceUnavailable)                         // first call
            .RespondJson(new User(2, "Second", "second@example.com"));          // then this

        using var client = mock.CreateClient(configure: b => b.Retry(1, TimeSpan.Zero));
        var api = new UsersApi(client);

        var first = await api.Get(1);
        var second = await api.Get(2);   // retried after the 503

        mock.VerifyAllCalled();
        Console.WriteLine(mock.Requests.Count);   // 3
        // end-snippet

        Expect(first.Name == "Mock User" && second.Name == "Second" && mock.Requests.Count == 3, "mock http");
    }
}
