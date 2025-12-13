# Getting Started with Nexar

Nexar is a powerful and intuitive HTTP client library for .NET that makes API requests simple and enjoyable.

## Installation

```bash
dotnet add package BuzzSpire.Nexar
```

## Quick Start

### 1. Simple GET Request (Static Method)

The easiest way to make a request:

```csharp
using Nexar;

// Option A: Simple string response (no generic type)
string response = await Nexar.Nexar.Get("https://api.example.com/users/1");
Console.WriteLine(response); // Raw JSON string

// Option B: Typed response (with automatic deserialization)
var response = await Nexar.Nexar.Get<User>("https://api.example.com/users/1");

if (response.IsSuccess)
{
    Console.WriteLine($"User: {response.Data.Name}");
    Console.WriteLine($"Status: {response.Status}"); // 200
}
else
{
    Console.WriteLine($"Error: {response.ErrorMessage}");
}
```

**When to use which?**
- Use **string version** when you need raw JSON or want to parse manually
- Use **typed version** when you want automatic deserialization and type safety

### 2. POST Request

```csharp
var newUser = new { Name = "John", Email = "john@example.com" };

// Option A: String response
string response = await Nexar.Nexar.Post(
    "https://api.example.com/users",
    newUser
);
Console.WriteLine($"Response: {response}");

// Option B: Typed response
var typedResponse = await Nexar.Nexar.Post<User>(
    "https://api.example.com/users",
    newUser
);

if (typedResponse.IsSuccess)
{
    Console.WriteLine($"Created user with ID: {typedResponse.Data.Id}");
}
```

### 3. Creating a Configured Instance

For multiple requests to the same API:

```csharp
var api = Nexar.Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" },
        { "Authorization", "Bearer your-token" }
    },
    TimeoutSeconds = 30
});

// Now use relative URLs
var user = await api.GetAsync<User>("/users/1");
var posts = await api.GetAsync<List<Post>>("/users/1/posts");
```

### 4. Using Fluent API

```csharp
var response = await Nexar.Nexar.Create()
    .Request()
    .Url("https://api.example.com/search")
    .WithHeader("Accept", "application/json")
    .WithQuery("q", "nexar")
    .WithQuery("limit", 10)
    .WithBearerToken("your-token")
    .GetAsync<SearchResults>();
```

### 5. Request Configuration Object

```csharp
var response = await Nexar.Nexar.Request<User>(new RequestOptions
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
var response = await Nexar.Nexar.Get<User>("/users/1");

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
var response = await Nexar.Nexar.Get<User>("/users/1");

if (!response.IsSuccess)
{
    Console.WriteLine($"Request failed: {response.Status} - {response.ErrorMessage}");
    if (response.Exception != null)
    {
        Console.WriteLine($"Exception: {response.Exception.Message}");
    }
    return;
}

// Use response.Data safely
Console.WriteLine(response.Data.Name);
```

### Authentication

```csharp
// Bearer Token
var response = await api.Request()
    .Url("/protected")
    .WithBearerToken("your-jwt-token")
    .GetAsync<Data>();

// Basic Auth
var response = await api.Request()
    .Url("/protected")
    .WithBasicAuth("username", "password")
    .GetAsync<Data>();

// API Key
var response = await api.Request()
    .Url("/protected")
    .WithApiKey("X-API-Key", "your-key")
    .GetAsync<Data>();
```

### Query Parameters

```csharp
var response = await api.Request()
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
    public async Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request)
    {
        Console.WriteLine($"→ {request.Method} {request.RequestUri}");
        return request;
    }

    public async Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response)
    {
        Console.WriteLine($"← {response.StatusCode}");
        return response;
    }

    public async Task OnErrorAsync(Exception exception)
    {
        Console.WriteLine($"✗ {exception.Message}");
    }
}

var api = Nexar.Nexar.Create();
api.Interceptors.Add(new LoggingInterceptor());
```

## Available Static Methods

Each method has two versions:

**String Response (Simple):**
- `Nexar.Get(url)` - GET request, returns string
- `Nexar.Post(url, data)` - POST request, returns string
- `Nexar.Put(url, data)` - PUT request, returns string
- `Nexar.Delete(url)` - DELETE request, returns string
- `Nexar.Patch(url, data)` - PATCH request, returns string
- `Nexar.Head(url)` - HEAD request, returns string

**Typed Response (Advanced):**
- `Nexar.Get<T>(url)` - GET request, returns NexarResponse<T>
- `Nexar.Post<T>(url, data)` - POST request, returns NexarResponse<T>
- `Nexar.Put<T>(url, data)` - PUT request, returns NexarResponse<T>
- `Nexar.Delete<T>(url)` - DELETE request, returns NexarResponse<T>
- `Nexar.Patch<T>(url, data)` - PATCH request, returns NexarResponse<T>
- `Nexar.Head<T>(url)` - HEAD request, returns NexarResponse<T>
- `Nexar.Request<T>(options)` - Request with config, returns NexarResponse<T>

## Advanced Configuration

### Retry with Exponential Backoff

```csharp
var api = Nexar.Nexar.Create(new NexarConfig
{
    MaxRetryAttempts = 3,
    RetryDelayMilliseconds = 1000,
    UseExponentialBackoff = true  // 1s, 2s, 4s
});
```

### Custom Timeout

```csharp
var api = Nexar.Nexar.Create(new NexarConfig
{
    TimeoutSeconds = 60
});
```

### Disable SSL Validation (Development Only)

```csharp
var api = Nexar.Nexar.Create(new NexarConfig
{
    ValidateSslCertificates = false  // Only for development!
});
```

## Complete Example

```csharp
using Nexar;
using Nexar.Configuration;

// Create configured client
var api = Nexar.Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" }
    },
    TimeoutSeconds = 30,
    MaxRetryAttempts = 3
});

try
{
    // GET request
    var user = await api.GetAsync<User>("/users/1");

    if (user.IsSuccess)
    {
        Console.WriteLine($"User: {user.Data.Name}");

        // POST request
        var newPost = new { Title = "Hello", Content = "World" };
        var post = await api.PostAsync<Post, PostResponse>("/posts", null, newPost);

        if (post.IsSuccess)
        {
            Console.WriteLine($"Created post: {post.Data.Id}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Next Steps

- Check out the [samples](../samples) directory for more examples
- Read the full [API Reference](../README.md#api-reference)
- Learn about [Interceptors](../README.md#interceptors)
- Explore [Authentication options](../README.md#authentication)

## Need Help?

- GitHub Issues: https://github.com/BuzzSpire/Nexar/issues
- Full Documentation: https://github.com/BuzzSpire/Nexar
