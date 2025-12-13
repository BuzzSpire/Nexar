using Nexar;
using Nexar.Configuration;
using Nexar.Interceptors;
using Nexar.Models;

partial class Program
{
    static async Task QueryParamsExample()
    {
        Console.WriteLine("6. Query Parameters:");

        // Using fluent API with query parameters
        var api = Nexar.Nexar.Create();
        var response = await api.Request()
            .Url("https://freetestapi.com/api/v1/destinations")
            .WithQuery("limit", "3")
            .WithQuery("page", "1")
            .GetAsync<object>();

        Console.WriteLine($"   Status: {response.Status}");
        Console.WriteLine($"   Request URL with params applied\n");
    }

    static async Task InterceptorExample()
    {
        Console.WriteLine("7. Request/Response Interceptors:");

        var api = Nexar.Nexar.Create();
        api.Interceptors.Add(new LoggingInterceptor());

        var response = await api.GetAsync<Destination>("https://freetestapi.com/api/v1/destinations/1");

        Console.WriteLine($"   Completed with interceptor\n");
    }

    // Backward compatible - old API still works
    static async Task LegacyApiExample()
    {
        Console.WriteLine("8. Legacy API (Backward Compatible):");

        var nexar = new Nexar.Nexar();
        var headers = new Dictionary<string, string>
        {
            { "Accept", "application/json" }
        };

        try
        {
            // Old style still works
            var response = await nexar.GetAsync("https://freetestapi.com/api/v1/destinations/1", headers);
            Console.WriteLine($"   Old API works: {response.Substring(0, Math.Min(50, response.Length))}...\n");
        }
        finally
        {
            nexar.Dispose();
        }
    }
}

public class Destination
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string country { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
}

public class LoggingInterceptor : IInterceptor
{
    public async Task<HttpRequestMessage> OnRequestAsync(HttpRequestMessage request)
    {
        Console.WriteLine($"   [Interceptor] → {request.Method} {request.RequestUri}");
        return await Task.FromResult(request);
    }

    public async Task<HttpResponseMessage> OnResponseAsync(HttpResponseMessage response)
    {
        Console.WriteLine($"   [Interceptor] ← {(int)response.StatusCode} {response.StatusCode}");
        return await Task.FromResult(response);
    }

    public async Task OnErrorAsync(Exception exception)
    {
        Console.WriteLine($"   [Interceptor] ✗ Error: {exception.Message}");
        await Task.CompletedTask;
    }
}
