# Getting Started with Nexar

Nexar is a lightweight and intuitive HTTP client library for .NET that makes API requests simple and ergonomic.

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

### 1. Simple GET Request (Static Method)

```csharp
var response = await Nexar.Get<User>("https://api.example.com/users/1");

if (response.IsSuccess && response.Data != null)
{
    Console.WriteLine($"User: {response.Data.Name}");
    Console.WriteLine($"Status: {response.Status}");
}
```

### 2. Raw Response Text

```csharp
var raw = await Nexar.Get<string>("https://api.example.com/users/1");
Console.WriteLine(raw.Data ?? raw.RawContent);
```

### 3. POST Request

```csharp
var newUser = new { Name = "John", Email = "john@example.com" };

var response = await Nexar.Post<User>(
    "https://api.example.com/users",
    newUser
);

if (response.IsSuccess && response.Data != null)
{
    Console.WriteLine($"Created user with ID: {response.Data.Id}");
}
```

### 4. Creating a Configured Instance

```csharp
var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" },
        { "Authorization", "Bearer your-token" }
    },
    TimeoutMs = 30_000
});

var user = await api.GetAsync<User>("/users/1");
var posts = await api.GetAsync<List<Post>>("/users/1/posts");
```

### 5. Using the Fluent API

```csharp
var response = await Nexar.Create()
    .Request()
    .Url("https://api.example.com/search")
    .WithHeader("Accept", "application/json")
    .WithQuery("q", "nexar")
    .WithQuery("limit", 10)
    .WithBearerToken("your-token")
    .GetAsync<SearchResults>();
```

### 6. RequestOptions

```csharp
var response = await Nexar.Request<User>(new RequestOptions
{
    Method = "GET",
    Url = "https://api.example.com/users/1",
    Headers = new Dictionary<string, string>
    {
        { "Authorization", "Bearer token" }
    },
    Timeout = 5000
});
```

## Response Structure

All responses include:

```csharp
var response = await Nexar.Get<User>("/users/1");

response.Data           // Deserialized data (User object)
response.Status         // HTTP status code (200)
response.StatusText     // Status message ("OK")
response.Headers        // Response headers
response.IsSuccess      // Success indicator (true/false)
response.RawContent     // Raw response string
response.ErrorMessage   // Error message if failed
```

## Common Patterns

### Error Handling

```csharp
var response = await Nexar.Get<User>("/users/1");

if (!response.IsSuccess)
{
    Console.WriteLine($"Request failed: {response.Status} - {response.ErrorMessage}");
    if (response.Exception != null)
    {
        Console.WriteLine($"Exception: {response.Exception.Message}");
    }
    return;
}

Console.WriteLine(response.Data?.Name);
```

### Authentication

```csharp
// Bearer Token
var response = await Nexar.Create()
    .Request()
    .Url("/protected")
    .WithBearerToken("your-jwt-token")
    .GetAsync<Data>();

// Basic Auth
var response = await Nexar.Create()
    .Request()
    .Url("/protected")
    .WithBasicAuth("username", "password")
    .GetAsync<Data>();

// API Key
var response = await Nexar.Create()
    .Request()
    .Url("/protected")
    .WithApiKey("X-API-Key", "your-key")
    .GetAsync<Data>();
```

### Query Parameters

```csharp
var response = await Nexar.Create()
    .Request()
    .Url("/users")
    .WithQuery("page", 1)
    .WithQuery("limit", 20)
    .WithQuery("sort", "name")
    .GetAsync<List<User>>();

// Generates: /users?page=1&limit=20&sort=name
```

### Using Interceptors

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

## Available Static Methods

All static methods return `NexarResponse<T>`:

- `Nexar.Get<T>(url, options?)`
- `Nexar.Post<T>(url, data?, options?)`
- `Nexar.Put<T>(url, data?, options?)`
- `Nexar.Delete<T>(url, options?)`
- `Nexar.Patch<T>(url, data?, options?)`
- `Nexar.Head<T>(url, options?)`
- `Nexar.Request<T>(options)`

## Advanced Configuration

### Retry with Exponential Backoff

```csharp
var api = Nexar.Create(new NexarConfig
{
    MaxRetryAttempts = 3,
    RetryDelayMilliseconds = 1000,
    UseExponentialBackoff = true
});
```

### Custom Timeout

```csharp
var api = Nexar.Create(new NexarConfig
{
    TimeoutMs = 60_000
});
```

### Disable SSL Validation (Development Only)

```csharp
var api = Nexar.Create(new NexarConfig
{
    ValidateSslCertificates = false
});
```

## Complete Example

```csharp
using Nexar;
using Nexar.Configuration;

var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" }
    },
    TimeoutMs = 30_000,
    MaxRetryAttempts = 3
});

try
{
    var user = await api.GetAsync<User>("/users/1");

    if (user.IsSuccess)
    {
        Console.WriteLine($"User: {user.Data?.Name}");

        var newPost = new { Title = "Hello", Content = "World" };
        var post = await api.PostAsync<object, PostResponse>("/posts", null, newPost);

        if (post.IsSuccess)
        {
            Console.WriteLine($"Created post: {post.Data?.Id}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Next Steps

- Check out the `../samples` directory for more examples
- Read the full API reference in `../README.md`

## Need Help?

- GitHub Issues: https://github.com/BuzzSpire/Nexar/issues
- Full Documentation: https://github.com/BuzzSpire/Nexar
