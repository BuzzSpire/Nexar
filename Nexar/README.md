![Nexar](https://socialify.git.ci/BuzzSpire/Nexar/image?description=1&descriptionEditable=Nexar%20is%20a%20C%23%20class%20used%20for%20sending%20and%20receiving%20HTTP%20requests.&font=Jost&forks=1&issues=1&language=1&name=1&owner=1&pattern=Circuit%20Board&pulls=1&stargazers=1&theme=Auto)
# Nexar
Nexar is a powerful and user-friendly C# class designed for seamless communication with web APIs. With Nexar, sending and receiving HTTP requests has never been easier. Whether you need to perform simple GET requests or more complex POST operations, Nexar provides a clean and efficient interface to interact with web services.

## Features

- **Simple API**: Easy-to-use methods for all major HTTP verbs (GET, POST, PUT, DELETE, PATCH, HEAD).
- **Asynchronous Operations**: Built-in support for asynchronous requests, ensuring your applications remain responsive.
- **Customizable Headers**: Easily manage request headers for enhanced control over your API interactions.
- **Error Handling**: Robust error handling to manage exceptions and keep your applications running smoothly.
- **Resource Management**: Automatically handles resource disposal to prevent memory leaks.

## Example Usage

Here’s a quick example to demonstrate how to use the Nexar class to send a GET request:

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

In this example, an instance of the `Nexar` class is created, headers are defined, and a GET request is sent to `https://api.example.com`. The response is printed to the console, while errors are gracefully handled.
