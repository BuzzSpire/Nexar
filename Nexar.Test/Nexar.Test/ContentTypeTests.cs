using Nexar.Models;
using System.Text;
using Xunit;

/// <summary>
/// Tests for different content types support (FormData, FormUrlEncoded, Binary).
/// </summary>
public class ContentTypeTests
{
    private readonly Nexar.Nexar _nexar;

    public ContentTypeTests()
    {
        _nexar = new Nexar.Nexar();
    }

    [Fact]
    public async Task PostAsync_WithFormUrlEncoded_SendsCorrectContentType()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            { "username", "testuser" },
            { "password", "testpass123" },
            { "email", "test@example.com" }
        };

        // Act
        var response = await _nexar.PostAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/post",
            null,
            formData,
            ContentType.FormUrlEncoded);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("application/x-www-form-urlencoded", response.RawContent);
    }

    [Fact]
    public async Task PostAsync_WithFormData_SendsMultipartFormData()
    {
        // Arrange
        var formData = new Dictionary<string, object>
        {
            { "name", "Test File" },
            { "description", "A test file upload" },
            { "file", Encoding.UTF8.GetBytes("This is test file content") }
        };

        // Act
        var response = await _nexar.PostAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/post",
            null,
            formData,
            ContentType.FormData);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("multipart/form-data", response.RawContent);
    }

    [Fact]
    public async Task PostAsync_WithBinaryData_SendsOctetStream()
    {
        // Arrange
        var binaryData = Encoding.UTF8.GetBytes("Binary content data for testing");

        // Act
        var response = await _nexar.PostAsync<byte[], string>(
            "https://httpbin.org/post",
            null,
            binaryData,
            ContentType.Binary);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotEmpty(response.RawContent);
    }

    [Fact]
    public async Task PostAsync_WithJsonContentType_SendsJson()
    {
        // Arrange
        var jsonData = new
        {
            name = "Test User",
            email = "test@example.com",
            age = 25
        };

        // Act
        var response = await _nexar.PostAsync<object, string>(
            "https://httpbin.org/post",
            null,
            jsonData,
            ContentType.Json);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("application/json", response.RawContent);
    }

    [Fact]
    public async Task PutAsync_WithFormUrlEncoded_SendsCorrectContentType()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            { "field1", "value1" },
            { "field2", "value2" }
        };

        // Act
        var response = await _nexar.PutAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/put",
            null,
            formData,
            ContentType.FormUrlEncoded);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("application/x-www-form-urlencoded", response.RawContent);
    }

    [Fact]
    public async Task PatchAsync_WithFormData_SendsMultipartFormData()
    {
        // Arrange
        var formData = new Dictionary<string, object>
        {
            { "title", "Updated Title" },
            { "content", "Updated Content" }
        };

        // Act
        var response = await _nexar.PatchAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/patch",
            null,
            formData,
            ContentType.FormData);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("multipart/form-data", response.RawContent);
    }

    [Fact]
    public async Task Request_WithFormUrlEncodedInOptions_SendsCorrectContentType()
    {
        // Arrange
        var options = new RequestOptions
        {
            Url = "https://httpbin.org/post",
            Method = "POST",
            Data = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            },
            ContentType = ContentType.FormUrlEncoded
        };

        // Act
        var response = await Nexar.Nexar.Request<string>(options);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("application/x-www-form-urlencoded", response.RawContent);
    }

    [Fact]
    public async Task Request_WithFormDataInOptions_SendsMultipartFormData()
    {
        // Arrange
        var options = new RequestOptions
        {
            Url = "https://httpbin.org/post",
            Method = "POST",
            Data = new Dictionary<string, object>
            {
                { "field", "value" },
                { "file", Encoding.UTF8.GetBytes("file content") }
            },
            ContentType = ContentType.FormData
        };

        // Act
        var response = await Nexar.Nexar.Request<string>(options);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("multipart/form-data", response.RawContent);
    }

    [Fact]
    public async Task Request_WithBinaryInOptions_SendsBinaryData()
    {
        // Arrange
        var binaryContent = Encoding.UTF8.GetBytes("Binary test data");
        var options = new RequestOptions
        {
            Url = "https://httpbin.org/post",
            Method = "POST",
            Data = binaryContent,
            ContentType = ContentType.Binary
        };

        // Act
        var response = await Nexar.Nexar.Request<string>(options);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotEmpty(response.RawContent);
    }

    [Fact]
    public async Task PostAsync_WithFormUrlEncoded_HandlesSpecialCharacters()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            { "text", "Hello World!" },
            { "special", "value with spaces & symbols @#$" },
            { "unicode", "テスト" }
        };

        // Act
        var response = await _nexar.PostAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/post",
            null,
            formData,
            ContentType.FormUrlEncoded);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task PostAsync_WithEmptyFormData_HandlesEmptyData()
    {
        // Arrange
        var formData = new Dictionary<string, string>();

        // Act
        var response = await _nexar.PostAsync<Dictionary<string, string>, string>(
            "https://httpbin.org/post",
            null,
            formData,
            ContentType.FormUrlEncoded);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task PostAsync_WithStream_SendsBinaryData()
    {
        // Arrange
        var streamData = new MemoryStream(Encoding.UTF8.GetBytes("Stream test content"));

        // Act
        var response = await _nexar.PostAsync<Stream, string>(
            "https://httpbin.org/post",
            null,
            streamData,
            ContentType.Binary);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task PostAsync_WithFormData_HandlesStreamContent()
    {
        // Arrange
        var streamContent = new MemoryStream(Encoding.UTF8.GetBytes("File stream content"));
        var formData = new Dictionary<string, object>
        {
            { "filename", "test.txt" },
            { "file", streamContent }
        };

        // Act
        var response = await _nexar.PostAsync<Dictionary<string, object>, string>(
            "https://httpbin.org/post",
            null,
            formData,
            ContentType.FormData);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("multipart/form-data", response.RawContent);
    }
}
