# Getting Started with Nexar

This guide walks through a typical API integration step by step. For the full reference, see the [README](../README.md).

## 1. Install

```bash
dotnet add package BuzzSpire.Nexar
```

Everything lives in a single namespace:

```csharp
using Nexar;
```

## 2. Create one client

Create the client once (for example at startup, or as a singleton in DI) and reuse it for every request.

```csharp
var client = NexarClient.Builder()
    .BaseUrl("https://api.example.com/v1")
    .Timeout(TimeSpan.FromSeconds(15))
    .UserAgent("my-app/1.0")
    .Build();
```

## 3. Send a request and read JSON

```csharp
record User(int Id, string Name, string Email);

var user = await client.Get("/users/1")
    .Send()             // send, wait for headers
    .ErrorForStatus()   // turn 4xx/5xx into an exception
    .Json<User>();      // read and decode the body

Console.WriteLine(user.Name);
```

## 4. Add headers, auth and query parameters

```csharp
var users = await client.Get("/users")
    .BearerAuth(accessToken)
    .Header("X-Request-Id", Guid.NewGuid().ToString())
    .Query("page", 2)
    .Query("active", true)
    .Send()
    .ErrorForStatus()
    .Json<List<User>>();
```

## 5. Send data

```csharp
// JSON
var created = await client.Post("/users")
    .Json(new { Name = "Ada", Email = "ada@example.com" })
    .Send()
    .ErrorForStatus()
    .Json<User>();

// HTML form
await client.Post("/login").Form(new { Username = "ada", Password = "secret" }).Send();

// File upload
await client.Post("/avatars")
    .Multipart(new MultipartForm()
        .Text("userId", "1")
        .File("image", await File.ReadAllBytesAsync("me.png"), "me.png", "image/png"))
    .Send()
    .ErrorForStatus();
```

## 6. Look at the response before reading it

```csharp
using var res = await client.Get("/users/1").Send();

if (res.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine("No such user");
}
else
{
    await res.ErrorForStatus();
    var user = await res.Json<User>();
}
```

## 7. Handle errors

```csharp
try
{
    var user = await client.Get("/users/1").Send().ErrorForStatus().Json<User>();
}
catch (NexarException e)
{
    switch (e.Kind)
    {
        case ErrorKind.Status:  Console.WriteLine($"Server said {e.StatusCode}"); break;
        case ErrorKind.Timeout: Console.WriteLine("Too slow");                    break;
        case ErrorKind.Connect: Console.WriteLine("Server unreachable");          break;
        case ErrorKind.Decode:  Console.WriteLine("Unexpected response body");    break;
        default:                Console.WriteLine(e.Message);                     break;
    }
}
```

## 8. Make it resilient

```csharp
var client = NexarClient.Builder()
    .BaseUrl("https://api.example.com/v1")
    .Retry(maxRetries: 3, delay: TimeSpan.FromMilliseconds(200))
    .AddHandler(new LoggingHandler())   // any DelegatingHandler
    .Build();
```

## 9. Use it with dependency injection

```bash
dotnet add package BuzzSpire.Nexar.Extensions.DependencyInjection
```

```csharp
builder.Services.AddNexarClient<ExampleApi>(b => b.BaseUrl("https://api.example.com/v1"));

public sealed class ExampleApi(NexarClient http)
{
    public Task<User> GetUser(int id) =>
        http.Get("/users/{id}").Path("id", id).Send().ErrorForStatus().Json<User>();
}
```
