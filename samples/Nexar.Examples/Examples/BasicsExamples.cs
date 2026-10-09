using System.Net;
using System.Text;
using static Nexar.Examples.Example;

namespace Nexar.Examples;

public static class BasicsExamples
{
    public static async Task QuickStart(string baseUrl)
    {
        // snippet:quick-start
        using var client = NexarClient.Builder()
            .BaseUrl(baseUrl)
            .Timeout(TimeSpan.FromSeconds(30))
            .UserAgent("my-app/1.0")
            .Build();

        var user = await client.Get("/users/{id}")
            .Path("id", 1)
            .Send()
            .ErrorForStatus()
            .Json<User>();

        Console.WriteLine(user.Name);   // Ada Lovelace
        // end-snippet

        Expect(user.Name == "Ada Lovelace", "quick start returns Ada");
    }

    public static async Task Requests(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

        // snippet:requests-query-headers
        var matches = await client.Get("/users")
            .Query("name", "ada")
            .Header("X-Request-Id", Guid.NewGuid().ToString())
            .Accept("application/json")
            .Send()
            .ErrorForStatus()
            .Json<List<User>>();
        // end-snippet

        // snippet:requests-json-body
        using var created = await client.Post("/users")
            .Json(new NewUser("Grace Hopper", "grace@example.com"))
            .Send()
            .ErrorForStatus();

        Console.WriteLine(created.StatusCode);   // Created
        Console.WriteLine(created.Location);     // http://.../users/3
        var grace = await created.Json<User>();
        // end-snippet

        // snippet:requests-form-body
        var login = await client.Post("/login")
            .Form(new Dictionary<string, string> { ["username"] = "ada", ["password"] = "secret" })
            .Send()
            .ErrorForStatus()
            .Text();
        // end-snippet

        // snippet:requests-multipart
        var upload = await client.Post("/upload")
            .Multipart(new MultipartForm()
                .Text("title", "Quarterly report")
                .File("file", Encoding.UTF8.GetBytes("a,b\n1,2"), "report.csv", "text/csv"))
            .Send()
            .ErrorForStatus()
            .Text();
        // end-snippet

        Expect(matches.Single().Name == "Ada Lovelace", "query filters users");
        Expect(created.StatusCode == HttpStatusCode.Created && grace.Id == 3, "user created");
        Expect(login.Contains("demo-token"), "form login");
        Expect(upload.Contains("report.csv"), "multipart upload");
    }

    public static async Task Responses(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

        // snippet:responses
        using var response = await client.Get("/users/1").Send();

        Console.WriteLine(response.StatusCode);              // OK
        Console.WriteLine(response.IsSuccess);               // True
        Console.WriteLine(response.ContentType?.MediaType);  // application/json
        Console.WriteLine(response.Header("Date"));           // any header, or null

        string text = await response.Text();        // the body is read on demand...
        User user = await response.Json<User>();    // ...and buffered, so it can be read again
        byte[] bytes = await response.Bytes();
        // end-snippet

        Expect(text.Contains("Ada") && user.Id == 1 && bytes.Length == text.Length, "body read three ways");
    }

    public static async Task Errors(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();

        // snippet:errors
        try
        {
            await client.Get("/users/42").Send().ErrorForStatus();
        }
        catch (NexarException e) when (e.IsStatus)
        {
            Console.WriteLine(e.StatusCode);       // NotFound
            Console.WriteLine(e.Problem?.Title);   // User not found (RFC 9457 problem details)
            Console.WriteLine(e.ResponseBody);     // the raw error body
        }

        try
        {
            await client.Post("/users").Json(new NewUser("", "nobody@example.com")).Send().ErrorForStatus();
        }
        catch (NexarException e) when (e.Problem is { } problem)
        {
            var errors = problem.Extension<Dictionary<string, string[]>>("errors");
            Console.WriteLine(errors?["Name"][0]);   // The Name field is required.
        }

        try
        {
            await client.Get("/slow").Timeout(TimeSpan.FromMilliseconds(200)).Send();
        }
        catch (NexarException e) when (e.IsTimeout)
        {
            Console.WriteLine(e.Kind);   // Timeout
        }
        // end-snippet

        var notFound = await Catch(() => client.Get("/users/42").Send().ErrorForStatus());
        var invalid = await Catch(() => client.Post("/users").Json(new NewUser("", "x")).Send().ErrorForStatus());
        var slow = await Catch(() => client.Get("/slow").Timeout(TimeSpan.FromMilliseconds(200)).Send());
        Expect(notFound.Problem?.Title == "User not found", "problem details title");
        Expect(invalid.Problem?.Extension<Dictionary<string, string[]>>("errors")?["Name"][0] == "The Name field is required.", "validation errors");
        Expect(slow.IsTimeout, "timeout");
    }

    private static async Task<NexarException> Catch(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (NexarException e)
        {
            return e;
        }
        throw new InvalidOperationException("Expected a NexarException.");
    }

    public static async Task Retries(string baseUrl)
    {
        // snippet:retries
        using var client = NexarClient.Builder()
            .BaseUrl(baseUrl)
            .Retry(maxRetries: 3, delay: TimeSpan.FromMilliseconds(100))   // 100, 200, 400 ms
            .Build();

        // /flaky answers 503 twice, then 200
        var text = await client.Get("/flaky").Send().ErrorForStatus().Text();
        // end-snippet

        Expect(text.Contains("finally"), "retried until success");
    }

    public static async Task BuildAndSign(string baseUrl)
    {
        using var client = NexarClient.Builder().BaseUrl(baseUrl).Build();
        var key = "signing-key"u8.ToArray();

        // snippet:build-and-sign
        var request = client.Post("/users").Json(new NewUser("Edsger Dijkstra", "edsger@example.com")).Build();

        var body = await request.ReadBodyAsync();   // the exact bytes that will be sent
        var signature = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(key, body!));
        request.SetHeader("X-Signature", signature);

        using var response = await client.Execute(request);
        // end-snippet

        Expect(response.StatusCode == HttpStatusCode.Created, "signed request accepted");
    }
}
