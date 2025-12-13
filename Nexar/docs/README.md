# Nexar

Nexar is a powerful and user-friendly C# HTTP client library. It provides a clean and intuitive API for making HTTP requests in .NET applications, making web API communication effortless and enjoyable.

## Features

- **Fluent API**: Chainable methods for building requests with ease
- **Typed Responses**: Generic response types with automatic JSON deserialization
- **Configuration**: BaseURL, default headers, timeout, and more
- **Interceptors**: Request and response interceptors for logging, authentication, etc.
- **Retry Mechanism**: Automatic retry with exponential backoff
- **Authentication Helpers**: Built-in support for Bearer tokens, Basic Auth, and API keys
- **Query String Builder**: Easy-to-use query parameter construction
- **Asynchronous**: Fully async/await support for responsive applications
- **Error Handling**: Comprehensive error handling with detailed response information

## Installation

```bash
dotnet add package BuzzSpire.Nexar
```

## Quick Start

### Basic GET Request

```csharp
// Simple: Just get the raw JSON string
string response = await Nexar.Get("https://api.example.com/users/1");
Console.WriteLine(response);

// With typed response
var response = await Nexar.Get<User>("https://api.example.com/users/1");
if (response.IsSuccess && response.Data != null)
{
    Console.WriteLine($"User: {response.Data.Name}");
    Console.WriteLine($"Status: {response.Status}");
}
```

### POST Request

```csharp
var newUser = new { Name = "John", Email = "john@example.com" };

// Simple: Returns string
string response = await Nexar.Post("https://api.example.com/users", newUser);

// Typed: Returns NexarResponse<User>
var response = await Nexar.Post<User>("https://api.example.com/users", newUser);
if (response.IsSuccess)
{
    Console.WriteLine($"Created: {response.Data.Id}");
}
```

### Fluent API

```csharp
var api = Nexar.Create();

// Get typed response
var response = await api.Request()
    .Url("https://api.example.com/users")
    .WithHeader("Accept", "application/json")
    .WithQuery("page", "1")
    .WithQuery("limit", "10")
    .GetAsync<List<User>>();

// Or just get the raw string
// (fluent API also supports string responses through instance methods)
```

## Advanced Usage

### Configuration

```csharp
var config = new NexarConfig
{
    BaseUrl = "https://api.example.com",
    DefaultHeaders = new Dictionary<string, string>
    {
        { "Accept", "application/json" }
    },
    TimeoutSeconds = 30,
    MaxRetryAttempts = 3
};

var nexar = new Nexar(config);
var response = await nexar.GetAsync<User>("/users/1");
```

### Authentication

```csharp
// Bearer Token
var response = await nexar.Request()
    .Url("/api/protected")
    .WithBearerToken("your-jwt-token")
    .GetAsync<Data>();

// Basic Auth
var response = await nexar.Request()
    .Url("/api/protected")
    .WithBasicAuth("username", "password")
    .GetAsync<Data>();
```

### POST Request

```csharp
var user = new User
{
    Name = "John Doe",
    Email = "john@example.com"
};

var response = await nexar.Request()
    .Url("/api/users")
    .PostAsync<User, UserResponse>(user);
```

For more examples, visit: https://github.com/BuzzSpire/Nexar

## License

This project is licensed under the MIT License.
