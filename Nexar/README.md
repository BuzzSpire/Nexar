![Nexar](https://socialify.git.ci/BuzzSpire/Nexar/image?description=1&descriptionEditable=Nexar%20is%20a%20C%23%20class%20used%20for%20sending%20and%20receiving%20HTTP%20requests.&font=Jost&forks=1&issues=1&language=1&name=1&owner=1&pattern=Circuit%20Board&pulls=1&stargazers=1&theme=Auto)

# Nexar

Nexar is a lightweight and ergonomic HTTP client for .NET. It offers static helpers, a fluent request builder, typed responses, and first-class support for common web API needs.

## Features

- **Static Methods**: `Nexar.Get<T>()`, `Nexar.Post<T>()`, `Nexar.Put<T>()`, `Nexar.Delete<T>()`, `Nexar.Patch<T>()`, `Nexar.Head<T>()`
- **Instance API**: `Nexar.Create()` plus `GetAsync<T>()`, `PostAsync<TRequest, TResponse>()`, and more
- **Fluent Builder**: Chain headers, query parameters, and body configuration
- **Typed Responses**: `NexarResponse<T>` with `Data`, `Status`, `Headers`, `RawContent`
- **Content Types**: JSON, form URL encoded, multipart form data, binary
- **Interceptors**: Request, response, and error hooks
- **Retries**: Optional retries with exponential backoff
- **Authentication Helpers**: Bearer token, Basic auth, API key helpers

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
    Console.WriteLine($"Status: {response.Status}");
}

// Need raw text? Use string as the response type.
var raw = await Nexar.Get<string>("https://api.example.com/users/1");
Console.WriteLine(raw.Data ?? raw.RawContent);
```

### Creating an Instance

```csharp
var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" },
        { "User-Agent", "Nexar/1.0" }
    },
    TimeoutMs = 30_000,
    MaxRetryAttempts = 3
});

var user = await api.GetAsync<User>("/users/1");
```

### Fluent Request Builder

```csharp
var response = await Nexar.Create()
    .Request()
    .Url("https://api.example.com/search")
    .WithHeader("Accept", "application/json")
    .WithQuery("q", "nexar")
    .WithQuery("limit", 10)
    .GetAsync<SearchResults>();
```

### RequestOptions

```csharp
var response = await Nexar.Request<User>(new RequestOptions
{
    Method = "POST",
    Url = "/users",
    BaseURL = "https://api.example.com",
    Headers = new Dictionary<string, string>
    {
        { "Authorization", "Bearer token" }
    },
    Data = new { Name = "John" },
    ContentType = ContentType.Json,
    Timeout = 5_000,
    MaxRetries = 2,
    ValidateSsl = true
});
```

## Response Structure

```csharp
var response = await Nexar.Get<User>("/users/1");

Console.WriteLine(response.Data);        // Deserialized object
Console.WriteLine(response.Status);      // HTTP status code
Console.WriteLine(response.StatusText);  // Status message
Console.WriteLine(response.Headers);     // Response headers
Console.WriteLine(response.IsSuccess);   // true/false
Console.WriteLine(response.RawContent);  // Raw response body
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

**Available Content Types:**
- `ContentType.Json`
- `ContentType.FormUrlEncoded`
- `ContentType.FormData`
- `ContentType.Binary`

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

## Authentication

```csharp
var response = await Nexar.Create()
    .Request()
    .Url("/protected")
    .WithBearerToken("your-jwt-token")
    .GetAsync<Data>();
```

## Retry With Exponential Backoff

```csharp
var api = Nexar.Create(new NexarConfig
{
    MaxRetryAttempts = 3,
    RetryDelayMilliseconds = 1000,
    UseExponentialBackoff = true
});
```

## Examples and Docs

- Samples: `../samples`
- Getting Started: `../docs/GetStarted.md`

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.
