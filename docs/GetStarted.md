# Nexar Usage

Nexar is a C# class used for sending and receiving HTTP requests.

## Example Usage

Below is an example of how to use the Nexar class to send a GET request:

```csharp
var nexar = new Nexar();

var headers = new Dictionary<string, string>
{
    { "Accept", "application/json" },
};

try
{
    var response = await nexar.GetAsync("https://api.example.com", headers);
    Console.WriteLine(response);
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}
finally
{
    nexar.Dispose();
}
```

In this example, an instance of the `Nexar` class is created, a dictionary of headers is set up, and then the `GetAsync` method is used to send a GET request to `https://api.example.com`. The response is printed to the console. If an error occurs during the request, it's caught, and the error message is printed to the console. Finally, the `Nexar` instance is disposed of to free up resources.

## Methods

The `Nexar` class includes the following methods:

- `GetAsync(string url, Dictionary<string, string> headers)`
- `PostAsync(string url, Dictionary<string, string> headers, string body)`
- `PutAsync(string url, Dictionary<string, string> headers, string body)`
- `DeleteAsync(string url, Dictionary<string, string> headers)`
- `PatchAsync(string url, Dictionary<string, string> headers, string body)`
- `HeadAsync(string url, Dictionary<string, string> headers)`

Each method is used to send a specific HTTP request. Each method takes a URL and headers. The `PostAsync`, `PutAsync`, and `PatchAsync` methods also take a body.
