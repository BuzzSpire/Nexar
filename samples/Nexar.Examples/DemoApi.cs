using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nexar.Examples;

public record User(int Id, string Name, string Email);

public record NewUser(string Name, string Email);

public record Repo(int Id, string Name);

public record LogEntry(string Level, string Message);

/// <summary>
/// A small local API the examples talk to, so they run offline and deterministically.
/// </summary>
public sealed class DemoApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _flakyCalls;
    private int _catalogVersion = 1;

    private DemoApi(WebApplication app)
    {
        _app = app;
    }

    public string BaseUrl { get; private set; } = "";

    public static async Task<DemoApi> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var api = new DemoApi(app);
        api.MapRoutes();
        await app.StartAsync();
        api.BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return api;
    }

    private void MapRoutes()
    {
        var users = new List<User> { new(1, "Ada Lovelace", "ada@example.com"), new(2, "Alan Turing", "alan@example.com") };

        _app.MapGet("/users", (string? name) => users.Where(u => name == null || u.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        _app.MapGet("/users/{id:int}", (int id) => users.FirstOrDefault(u => u.Id == id) is { } user
            ? Results.Ok(user)
            : Results.Problem(title: "User not found", detail: $"There is no user {id}.", statusCode: 404));
        _app.MapPost("/users", (NewUser user) =>
        {
            if (string.IsNullOrWhiteSpace(user.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = ["The Name field is required."] });
            }
            var created = new User(users.Max(u => u.Id) + 1, user.Name, user.Email);
            users.Add(created);
            return Results.Created($"/users/{created.Id}", created);
        });

        _app.MapPost("/login", (HttpRequest request) =>
            request.Form["username"] == "ada" && request.Form["password"] == "secret"
                ? Results.Ok(new { token = "demo-token" })
                : Results.Unauthorized());

        _app.MapPost("/upload", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return Results.Ok(new { title = form["title"].ToString(), files = form.Files.Select(f => new { f.FileName, f.Length, f.ContentType }) });
        }).DisableAntiforgery();

        _app.MapGet("/me", (HttpRequest request) => request.Headers.Authorization == "Bearer demo-token"
            ? Results.Ok(users[0])
            : Results.Unauthorized());

        _app.MapPost("/oauth/token", (HttpRequest request) =>
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("reports-app:s3cret"));
            return request.Headers.Authorization == $"Basic {basic}" && request.Form["grant_type"] == "client_credentials"
                ? Results.Ok(new { access_token = "oauth-token", token_type = "Bearer", expires_in = 3600 })
                : Results.Json(new { error = "invalid_client" }, statusCode: 401);
        });
        _app.MapGet("/reports/daily", (HttpRequest request) => request.Headers.Authorization == "Bearer oauth-token"
            ? Results.Ok(new { visits = 1234 })
            : Results.Unauthorized());

        _app.MapGet("/flaky", () => Interlocked.Increment(ref _flakyCalls) % 3 == 0
            ? Results.Ok("finally")
            : Results.StatusCode(503));
        _app.MapGet("/slow", async (CancellationToken ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Results.Ok();
        });

        _app.MapGet("/repos", (HttpContext context, int page = 1) =>
        {
            if (page < 3)
            {
                context.Response.Headers.Link = $"</repos?page={page + 1}>; rel=\"next\"";
            }
            return Enumerable.Range(1, 2).Select(i => new Repo((page - 1) * 2 + i, $"repo-{(page - 1) * 2 + i}"));
        });

        _app.MapGet("/events", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            foreach (var word in new[] { "Hello", " from", " Nexar" })
            {
                await context.Response.WriteAsync($"event: delta\ndata: {word}\n\n");
                await context.Response.Body.FlushAsync();
            }
            await context.Response.WriteAsync("event: done\ndata: [DONE]\n\n");
        });

        _app.MapGet("/logs", async (HttpContext context) =>
        {
            context.Response.ContentType = "application/x-ndjson";
            await context.Response.WriteAsync("{\"level\":\"info\",\"message\":\"started\"}\n{\"level\":\"warn\",\"message\":\"disk at 80%\"}\n");
        });

        _app.MapGet("/files/report.csv", () =>
            Results.Bytes(Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"{i},row {i}"))),
                "text/csv", enableRangeProcessing: true));

        _app.MapGet("/catalog", (HttpContext context) =>
        {
            var etag = $"\"v{_catalogVersion}\"";
            context.Response.Headers.CacheControl = "max-age=60";
            context.Response.Headers.ETag = etag;
            return context.Request.Headers.IfNoneMatch == etag
                ? Results.StatusCode(304)
                : Results.Ok(new[] { "keyboard", "mouse" });
        });
    }

    public void ChangeCatalog() => Interlocked.Increment(ref _catalogVersion);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
