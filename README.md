![Nexar](https://socialify.git.ci/BuzzSpire/Nexar/image?description=1&descriptionEditable=Nexar%20is%20a%20C%23%20class%20used%20for%20sending%20and%20receiving%20HTTP%20requests.&font=Jost&forks=1&issues=1&language=1&name=1&owner=1&pattern=Circuit%20Board&pulls=1&stargazers=1&theme=Auto)

# Nexar

A powerful and intuitive HTTP client library for .NET. Write HTTP requests with a clean, modern API that feels natural and easy to use.

```csharp
// Simple and clean
var response = await Nexar.Get<User>("https://api.example.com/users/1");
Console.WriteLine(response.Data.Name);
```

## Features

- **Static Methods**: `Nexar.Get()`, `Nexar.Post()`, `Nexar.Put()`, `Nexar.Delete()`, etc.
- **Instance Creation**: `Nexar.Create()` for custom configurations
- **Typed Responses**: `response.Data`, `response.Status`, `response.Headers`
- **Request Configuration**: Flexible request options
- **Multiple Content Types**: JSON, Form Data, Form URL Encoded, Binary
- **Interceptors**: Request and response interceptors
- **Automatic JSON**: Serialization and deserialization
- **Retry Mechanism**: Exponential backoff support
- **Authentication**: Bearer tokens, Basic Auth, API keys
- **Fluent API**: Chainable request builder
- **Backward Compatible**: Legacy API still works

## Installation

```bash
dotnet add package BuzzSpire.Nexar
```

## Quick Start

### Static Methods

```csharp
// Option 1: Simple string response (no generic type needed)
string response = await Nexar.Get("https://api.example.com/users/1");
Console.WriteLine(response); // Raw JSON string

// Option 2: Typed response with automatic deserialization
var response = await Nexar.Get<User>("https://api.example.com/users/1");
if (response.IsSuccess)
{
    Console.WriteLine(response.Data.Name);
    Console.WriteLine($"Status: {response.Status}"); // 200
}

// POST - also works both ways
string rawResponse = await Nexar.Post("/users", new { Name = "John" });
var typedResponse = await Nexar.Post<User>("/users", new { Name = "John" });

// All methods support both versions
await Nexar.Put("/users/1", updatedData);          // Returns string
await Nexar.Put<User>("/users/1", updatedData);    // Returns NexarResponse<User>
await Nexar.Delete("/users/1");                     // Returns string
await Nexar.Delete<object>("/users/1");             // Returns NexarResponse<object>
```

### Creating Instances

```csharp
// Create an instance with custom configuration
var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" },
        { "User-Agent", "MyApp/1.0" }
    },
    TimeoutSeconds = 30,
    MaxRetryAttempts = 3
});

// Now use relative URLs
var user = await api.GetAsync<User>("/users/1");
```

### Request Configuration

```csharp
// Full request configuration
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

### Response Structure

```csharp
var response = await Nexar.Get<User>("/users/1");

// Access response properties
Console.WriteLine(response.Data);        // The deserialized object
Console.WriteLine(response.Status);      // 200
Console.WriteLine(response.StatusText);  // "OK"
Console.WriteLine(response.Headers);     // Response headers
Console.WriteLine(response.IsSuccess);   // true/false
```

## Examples

### GET Request

```csharp
// Simple - just get the JSON string
string json = await Nexar.Get("https://api.example.com/users/1");
Console.WriteLine(json);

// Typed - automatic deserialization
var response = await Nexar.Get<User>("https://api.example.com/users/1");
Console.WriteLine(response.Data.Name);
Console.WriteLine(response.Status);
```

### POST Request

```csharp
var response = await Nexar.Post<User>("/users", new {
    Name = "John",
    Email = "john@example.com"
});

if (response.IsSuccess)
{
    Console.WriteLine($"Created user: {response.Data.Id}");
}
```

### Creating Instance with Config

```csharp
var api = Nexar.Create(new NexarConfig {
    BaseUrl = "https://api.example.com",
    TimeoutSeconds = 5,
    DefaultHeaders = new Dictionary<string, string> {
        { "Authorization", "Bearer token" }
    }
});

var response = await api.GetAsync<User>("/users/1");
```

### Request with Config Object

```csharp
var response = await Nexar.Request<User>(new RequestOptions {
    Method = "POST",
    Url = "/users",
    Data = new { Name = "John" },
    Headers = new Dictionary<string, string> {
        { "Content-Type", "application/json" }
    }
});
```

## Advanced Features

### Interceptors

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

var api = Nexar.Create();
api.Interceptors.Add(new LoggingInterceptor());
```

### Authentication

```csharp
// Bearer Token
var api = Nexar.Create();
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

### Content Types

Nexar supports multiple content types for request bodies:

```csharp
// JSON (default)
var response = await Nexar.Post<User>("/users",
    new { Name = "John", Email = "john@example.com" });

// Form URL Encoded (application/x-www-form-urlencoded)
var formData = new Dictionary<string, string>
{
    { "username", "john" },
    { "password", "secret123" }
};
var response = await api.PostAsync<Dictionary<string, string>, string>(
    "/login", null, formData, ContentType.FormUrlEncoded);

// Multipart Form Data (multipart/form-data) - for file uploads
var formData = new Dictionary<string, object>
{
    { "title", "My Document" },
    { "file", fileBytes }  // byte[] or Stream
};
var response = await api.PostAsync<Dictionary<string, object>, string>(
    "/upload", null, formData, ContentType.FormData);

// Binary (application/octet-stream)
var binaryData = File.ReadAllBytes("image.png");
var response = await api.PostAsync<byte[], string>(
    "/upload-image", null, binaryData, ContentType.Binary);

// Using Fluent API
var response = await api.Request()
    .Url("/upload")
    .WithBody(formData)
    .WithContentType(ContentType.FormData)
    .PostAsync<string>();

// Using RequestOptions
var response = await Nexar.Request<string>(new RequestOptions
{
    Method = "POST",
    Url = "/upload",
    Data = formData,
    ContentType = ContentType.FormUrlEncoded
});
```

**Available Content Types:**
- `ContentType.Json` - JSON format (default)
- `ContentType.FormUrlEncoded` - Form URL encoded
- `ContentType.FormData` - Multipart form data
- `ContentType.Binary` - Binary/octet stream

### Retry with Exponential Backoff

```csharp
var api = Nexar.Create(new NexarConfig
{
    MaxRetryAttempts = 3,
    RetryDelayMilliseconds = 1000,
    UseExponentialBackoff = true  // 1s, 2s, 4s
});
```

## API Reference

### Static Methods

Each method has two versions:

**Simple (returns `string`):**
- `Nexar.Get(url, options?)` - GET request, returns raw string
- `Nexar.Post(url, data?, options?)` - POST request, returns raw string
- `Nexar.Put(url, data?, options?)` - PUT request, returns raw string
- `Nexar.Delete(url, options?)` - DELETE request, returns raw string
- `Nexar.Patch(url, data?, options?)` - PATCH request, returns raw string
- `Nexar.Head(url, options?)` - HEAD request, returns raw string

**Typed (returns `NexarResponse<T>`):**
- `Nexar.Get<T>(url, options?)` - GET with typed response
- `Nexar.Post<T>(url, data?, options?)` - POST with typed response
- `Nexar.Put<T>(url, data?, options?)` - PUT with typed response
- `Nexar.Delete<T>(url, options?)` - DELETE with typed response
- `Nexar.Patch<T>(url, data?, options?)` - PATCH with typed response
- `Nexar.Head<T>(url, options?)` - HEAD with typed response

**Advanced:**
- `Nexar.Request<T>(options)` - Request with full config

### Factory Methods

- `Nexar.Create()` - Create instance with default config
- `Nexar.Create(NexarConfig)` - Create instance with custom config
- `Nexar.Create(RequestOptions)` - Create instance from request options

### Response Properties

- `response.Data` - Deserialized response data
- `response.Status` - HTTP status code (int)
- `response.StatusText` - Status message
- `response.Headers` - Response headers
- `response.IsSuccess` - Success indicator
- `response.Config` - Request configuration used
- `response.RawContent` - Raw response string

### Configuration

```csharp
new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>(),
    TimeoutSeconds = 100,
    MaxRetryAttempts = 0,
    RetryDelayMilliseconds = 1000,
    UseExponentialBackoff = true,
    ValidateSslCertificates = true
}
```

### Request Options

```csharp
new RequestOptions
{
    Url = "/users",
    Method = "GET",
    Headers = new Dictionary<string, string>(),
    Params = new Dictionary<string, string>(),  // Query params
    Data = new { },  // Request body
    Timeout = 5000,
    BaseURL = "https://api.example.com",
    MaxRetries = 3
}
```

## Usage Patterns

### Simple Request

```csharp
// Quickest way to make a request
var data = await Nexar.Get<User>("/users/1");
```

### With Configuration

```csharp
// Create a configured instance for multiple requests
var api = Nexar.Create(new NexarConfig
{
    BaseUrl = "https://api.example.com"
});

var user = await api.GetAsync<User>("/users/1");
var posts = await api.GetAsync<List<Post>>("/posts");
```

### Error Handling

```csharp
var response = await Nexar.Get<User>("/users/1");

if (response.IsSuccess)
{
    Console.WriteLine($"User: {response.Data.Name}");
}
else
{
    Console.WriteLine($"Error {response.Status}: {response.ErrorMessage}");
}
```

## Examples

Check out the [samples](./samples) directory for complete working examples including:
- Static method usage
- Instance creation
- Request configuration
- Response handling
- Authentication
- Interceptors
- Retry mechanism

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.

---

Made with ❤️ for .NET developers
