using Nexar;

/// <summary>
/// Examples showing raw string responses
/// Tip: Run the sample runner in `samples/Nexar.samples` for a compact overview.
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

        // Use string as the response type to get raw JSON
        var response = await Nexar.Nexar.Get<string>("https://freetestapi.com/api/v1/destinations/1");
        var raw = response.Data ?? response.RawContent;

        Console.WriteLine($"   Raw response: {raw.Substring(0, Math.Min(100, raw.Length))}...");
        Console.WriteLine($"   Response length: {raw.Length} characters\n");
    }

    static async Task StringPostExample()
    {
        Console.WriteLine("2. POST with String Response:");

        var data = new
        {
            name = "Tokyo",
            country = "Japan"
        };

        // Use string as the response type to get raw JSON
        var response = await Nexar.Nexar.Post<string>("https://freetestapi.com/api/v1/destinations", data);
        var raw = response.Data ?? response.RawContent;

        Console.WriteLine($"   Response: {raw.Substring(0, Math.Min(80, raw.Length))}...\n");
    }

    static async Task ManualJsonParseExample()
    {
        Console.WriteLine("3. Manual JSON Parsing:");

        // Get raw JSON string
        var response = await Nexar.Nexar.Get<string>("https://freetestapi.com/api/v1/destinations/1");
        var jsonResponse = response.Data ?? response.RawContent;

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

        // Method 1: Raw string response using string type
        Console.WriteLine("Method 1: String response");
        var rawResponse = await Nexar.Nexar.Get<string>(url);
        var rawValue = rawResponse.Data ?? rawResponse.RawContent;
        Console.WriteLine($"   Type: {rawValue.GetType().Name}");
        Console.WriteLine($"   Value: {rawValue.Substring(0, Math.Min(50, rawValue.Length))}...\n");

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
