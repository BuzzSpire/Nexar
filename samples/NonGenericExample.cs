using Nexar;

/// <summary>
/// Examples showing non-generic (string) responses
/// </summary>
public class NonGenericExample
{
    public static async Task RunExamples()
    {
        Console.WriteLine("=== Non-Generic (String) Response Examples ===\n");

        // Example 1: Simple GET - returns raw string
        await SimpleStringGetExample();

        // Example 2: POST with string response
        await StringPostExample();

        // Example 3: Parsing JSON manually
        await ManualJsonParseExample();
    }

    static async Task SimpleStringGetExample()
    {
        Console.WriteLine("1. Simple GET - Raw String Response:");

        // No generic type needed - returns string directly
        string response = await Nexar.Nexar.Get("https://freetestapi.com/api/v1/destinations/1");

        Console.WriteLine($"   Raw response: {response.Substring(0, Math.Min(100, response.Length))}...");
        Console.WriteLine($"   Response length: {response.Length} characters\n");
    }

    static async Task StringPostExample()
    {
        Console.WriteLine("2. POST with String Response:");

        var data = new
        {
            name = "Tokyo",
            country = "Japan"
        };

        // Returns raw string response
        string response = await Nexar.Nexar.Post("https://freetestapi.com/api/v1/destinations", data);

        Console.WriteLine($"   Response: {response.Substring(0, Math.Min(80, response.Length))}...\n");
    }

    static async Task ManualJsonParseExample()
    {
        Console.WriteLine("3. Manual JSON Parsing:");

        // Get raw JSON string
        string jsonResponse = await Nexar.Nexar.Get("https://freetestapi.com/api/v1/destinations/1");

        // Parse it yourself if needed
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var nameElement))
            {
                Console.WriteLine($"   Name (manual parse): {nameElement.GetString()}");
            }

            Console.WriteLine($"   Full JSON: {jsonResponse.Substring(0, Math.Min(100, jsonResponse.Length))}...\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Parse error: {ex.Message}\n");
        }
    }
}

/// <summary>
/// Comparison: Generic vs Non-Generic
/// </summary>
public class ComparisonExample
{
    public static async Task ShowComparison()
    {
        Console.WriteLine("=== Generic vs Non-Generic Comparison ===\n");

        string url = "https://freetestapi.com/api/v1/destinations/1";

        // Method 1: Non-Generic (String) - Simple, raw response
        Console.WriteLine("Method 1: Non-Generic (returns string)");
        string rawResponse = await Nexar.Nexar.Get(url);
        Console.WriteLine($"   Type: {rawResponse.GetType().Name}");
        Console.WriteLine($"   Value: {rawResponse.Substring(0, Math.Min(50, rawResponse.Length))}...\n");

        // Method 2: Generic - Typed, automatic deserialization
        Console.WriteLine("Method 2: Generic (returns NexarResponse<T>)");
        var typedResponse = await Nexar.Nexar.Get<Destination>(url);
        Console.WriteLine($"   Type: {typedResponse.GetType().Name}");
        Console.WriteLine($"   Data.Name: {typedResponse.Data?.name ?? "N/A"}");
        Console.WriteLine($"   Status: {typedResponse.Status}");
        Console.WriteLine($"   IsSuccess: {typedResponse.IsSuccess}\n");
    }

    public class Destination
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
    }
}
