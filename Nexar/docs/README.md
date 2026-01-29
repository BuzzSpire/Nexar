# Nexar

Nexar is a lightweight and ergonomic HTTP client for .NET with static helpers, a fluent request builder, and typed responses.

## Installation

```bash
dotnet add package BuzzSpire.Nexar
```

## Quick Start

```csharp
using Nexar;
using Nexar.Configuration;
using Nexar.Models;
```

### Static Methods

```csharp
var response = await Nexar.Get<User>("https://api.example.com/users/1");
if (response.IsSuccess && response.Data != null)
{
    Console.WriteLine(response.Data.Name);
}
```

### Raw Response

```csharp
var raw = await Nexar.Get<string>("https://api.example.com/users/1");
Console.WriteLine(raw.Data ?? raw.RawContent);
```

### Fluent API

```csharp
var response = await Nexar.Create()
    .Request()
    .Url("https://api.example.com/search")
    .WithHeader("Accept", "application/json")
    .WithQuery("q", "nexar")
    .WithQuery("limit", 10)
    .GetAsync<SearchResults>();
```

## Configuration

```csharp
var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    TimeoutMs = 30_000,
    MaxRetryAttempts = 3
});
```

## Content Types

```csharp
var formData = new Dictionary<string, object>
{
    { "title", "My Document" },
    { "file", fileBytes }
};

var response = await api.PostAsync<Dictionary<string, object>, string>(
    "/upload",
    null,
    formData,
    ContentType.FormData);
```

## Interceptors

```csharp
public class LoggingInterceptor : IInterceptor
{
    public Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request)
    {
        Console.WriteLine($"-> {request.Method} {request.RequestUri}");
        return Task.FromResult(request);
    }

    public Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response)
    {
        Console.WriteLine($"<- {response.StatusCode}");
        return Task.FromResult(response);
    }

    public Task OnErrorAsync(Exception exception)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }
}

var api = Nexar.Create();
api.Interceptors.Add(new LoggingInterceptor());
```

## More Examples

- Samples: `../../samples`
- Getting Started: `../../docs/GetStarted.md`
