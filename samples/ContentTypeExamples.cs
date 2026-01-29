using Nexar;
using Nexar.Models;
using System.Text;

/// <summary>
/// Examples demonstrating the use of different content types in Nexar HTTP client.
/// Supports: JSON, FormUrlEncoded, FormData (multipart), and Binary.
/// </summary>
public class ContentTypeExamples
{
    public static async Task RunExamples()
    {
        var nexar = new Nexar.Nexar();

        // ===================================================================
        // Example 1: JSON Content Type (Default)
        // ===================================================================
        Console.WriteLine("=== Example 1: JSON Content Type ===");
        var jsonData = new
        {
            name = "John Doe",
            email = "john@example.com",
            age = 30
        };

        var jsonResponse = await nexar.PostAsync<object, string>(
            "https://httpbin.org/post",
            null,
            jsonData,
            ContentType.Json);

        Console.WriteLine($"Status: {jsonResponse.Status}");
        Console.WriteLine($"Success: {jsonResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 2: Form URL Encoded (application/x-www-form-urlencoded)
        // ===================================================================
        Console.WriteLine("=== Example 2: Form URL Encoded ===");
        var formUrlEncodedData = new Dictionary<string, string>
        {
            { "username", "testuser" },
            { "password", "securepass123" },
            { "remember", "true" }
        };

        var formResponse = await nexar.PostAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/post",
            null,
            formUrlEncodedData,
            ContentType.FormUrlEncoded);

        Console.WriteLine($"Status: {formResponse.Status}");
        Console.WriteLine($"Success: {formResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 3: Multipart Form Data (multipart/form-data)
        // Useful for file uploads with additional fields
        // ===================================================================
        Console.WriteLine("=== Example 3: Multipart Form Data ===");
        var multipartData = new Dictionary<string, object>
        {
            { "title", "My Document" },
            { "description", "Important file" },
            { "file", Encoding.UTF8.GetBytes("File content here...") },
            { "category", "documents" }
        };

        var multipartResponse = await nexar.PostAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/post",
            null,
            multipartData,
            ContentType.FormData);

        Console.WriteLine($"Status: {multipartResponse.Status}");
        Console.WriteLine($"Success: {multipartResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 4: Binary Content (application/octet-stream)
        // For sending raw binary data like images, PDFs, etc.
        // ===================================================================
        Console.WriteLine("=== Example 4: Binary Content ===");
        var binaryData = Encoding.UTF8.GetBytes("Raw binary content");

        var binaryResponse = await nexar.PostAsync<byte[], string>(
            "https://httpbin.org/post",
            null,
            binaryData,
            ContentType.Binary);

        Console.WriteLine($"Status: {binaryResponse.Status}");
        Console.WriteLine($"Success: {binaryResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 5: Using RequestOptions with Content Type
        // ===================================================================
        Console.WriteLine("=== Example 5: Using RequestOptions ===");
        var options = new RequestOptions
        {
            Url = "https://httpbin.org/post",
            Method = "POST",
            Data = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            },
            ContentType = ContentType.FormUrlEncoded,
            Headers = new Dictionary<string, string>
            {
                { "X-Custom-Header", "CustomValue" }
            }
        };

        var optionsResponse = await Nexar.Nexar.Request<string>(options);
        Console.WriteLine($"Status: {optionsResponse.Status}");
        Console.WriteLine($"Success: {optionsResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 6: Uploading a file with Stream
        // ===================================================================
        Console.WriteLine("=== Example 6: File Upload with Stream ===");
        using var fileStream = new MemoryStream(Encoding.UTF8.GetBytes("This is file content from stream"));

        var fileUploadData = new Dictionary<string, object>
        {
            { "filename", "document.txt" },
            { "file", fileStream }
        };

        var fileResponse = await nexar.PostAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/post",
            null,
            fileUploadData,
            ContentType.FormData);

        Console.WriteLine($"Status: {fileResponse.Status}");
        Console.WriteLine($"Success: {fileResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 7: PUT request with Form Data
        // ===================================================================
        Console.WriteLine("=== Example 7: PUT with Form Data ===");
        var updateData = new Dictionary<string, string>
        {
            { "field1", "updated_value1" },
            { "field2", "updated_value2" }
        };

        var putResponse = await nexar.PutAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/put",
            null,
            updateData,
            ContentType.FormUrlEncoded);

        Console.WriteLine($"Status: {putResponse.Status}");
        Console.WriteLine($"Success: {putResponse.IsSuccess}");
        Console.WriteLine();

        // ===================================================================
        // Example 8: PATCH request with Multipart Form Data
        // ===================================================================
        Console.WriteLine("=== Example 8: PATCH with Multipart Form Data ===");
        var patchData = new Dictionary<string, object>
        {
            { "status", "active" },
            { "updatedAt", DateTime.UtcNow.ToString("o") }
        };

        var patchResponse = await nexar.PatchAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/patch",
            null,
            patchData,
            ContentType.FormData);

        Console.WriteLine($"Status: {patchResponse.Status}");
        Console.WriteLine($"Success: {patchResponse.IsSuccess}");
        Console.WriteLine();

        Console.WriteLine("All examples completed!");
    }
}
