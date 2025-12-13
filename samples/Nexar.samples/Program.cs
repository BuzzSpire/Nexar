using Nexar;
using Nexar.Configuration;
using Nexar.Interceptors;
using Nexar.Models;

partial class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Nexar HTTP Client Examples ===\n");

        // Example 1: Static method usage
        await StaticMethodExample();

        // Example 2: Nexar.Create() - Instance with config
        await CreateInstanceExample();

        // Example 3: Request config
        await RequestConfigExample();

        // Example 4: Response structure (response.data, response.status)
        await ResponseStructureExample();

        // Example 5: POST with data
        await PostExample();

        // Example 6: Using params (query parameters)
        await QueryParamsExample();

        // Example 7: Interceptors
        await InterceptorExample();

        // Example 8: Legacy API (backward compatible)
        await LegacyApiExample();

        Console.WriteLine("\n=== All examples completed ===");
    }

    static async Task StaticMethodExample()
    {
        Console.WriteLine("1. Static Method:");

        var response = await Nexar.Nexar.Get<Destination>("https://freetestapi.com/api/v1/destinations/1");

        if (response.IsSuccess && response.Data != null)
        {
            Console.WriteLine($"   Status: {response.Status} ({response.StatusText})");
            Console.WriteLine($"   Data: {response.Data.name}, {response.Data.country}\n");
        }
        else
        {
            Console.WriteLine($"   Error: {response.ErrorMessage}\n");
        }
    }

    static async Task CreateInstanceExample()
    {
        Console.WriteLine("2. Nexar.Create() - Instance with config:");

        var api = Nexar.Nexar.Create(new NexarConfig
        {
            BaseUrl = "https://freetestapi.com",
            DefaultHeaders = new Dictionary<string, string>
            {
                { "Accept", "application/json" },
                { "User-Agent", "Nexar/1.0" }
            },
            TimeoutSeconds = 30
        });

        // Now use relative URLs
        var response = await api.GetAsync<Destination>("/api/v1/destinations/2");

        Console.WriteLine($"   Status: {response.Status}");
        Console.WriteLine($"   Success: {response.IsSuccess}\n");
    }

    static async Task RequestConfigExample()
    {
        Console.WriteLine("3. Request Config:");

        var response = await Nexar.Nexar.Request<Destination>(new RequestOptions
        {
            Method = "GET",
            Url = "https://freetestapi.com/api/v1/destinations/1",
            Headers = new Dictionary<string, string>
            {
                { "Accept", "application/json" }
            },
            Timeout = 5000
        });

        Console.WriteLine($"   Status: {response.Status}");
        Console.WriteLine($"   Data: {response.Data?.name ?? "N/A"}\n");
    }

    static async Task ResponseStructureExample()
    {
        Console.WriteLine("4. Response Structure (response.data, response.status):");

        var response = await Nexar.Nexar.Get<Destination>("https://freetestapi.com/api/v1/destinations/1");

        // Access response properties
        Console.WriteLine($"   response.status: {response.Status}");
        Console.WriteLine($"   response.statusText: {response.StatusText}");
        Console.WriteLine($"   response.data: {response.Data?.name ?? "N/A"}");
        Console.WriteLine($"   response.headers: {response.Headers.Count} headers");
        Console.WriteLine($"   Success: {response.IsSuccess}\n");
    }

    static async Task PostExample()
    {
        Console.WriteLine("5. POST with data:");

        var newDestination = new
        {
            name = "Paris",
            country = "France",
            description = "City of Light"
        };

        var response = await Nexar.Nexar.Post<object>(
            "https://freetestapi.com/api/v1/destinations",
            newDestination
        );

        Console.WriteLine($"   Status: {response.Status}");
        Console.WriteLine($"   Success: {response.IsSuccess}\n");
    }

}
