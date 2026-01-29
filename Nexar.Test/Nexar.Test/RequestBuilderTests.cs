using Nexar.Configuration;
using Nexar.Models;

namespace Nexar.Test;

/// <summary>
/// Unit tests for NexarRequestBuilder (Fluent API)
/// </summary>
public class RequestBuilderTests : IDisposable
{
    private readonly global::Nexar.Nexar _nexar;

    public RequestBuilderTests()
    {
        var config = new NexarConfig
        {
            BaseUrl = "https://jsonplaceholder.typicode.com"
        };
        _nexar = new global::Nexar.Nexar(config);
    }

    [Fact]
    public async Task RequestBuilder_WithUrl_MakesRequest()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .GetAsync<Post>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public async Task RequestBuilder_WithHeader_AddsHeader()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .WithHeader("Accept", "application/json")
            .GetAsync<Post>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task RequestBuilder_WithQuery_AddsQueryParameter()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts")
            .WithQuery("userId", "1")
            .GetAsync<Post[]>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public async Task RequestBuilder_WithMultipleQueries_AddsAllParameters()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts")
            .WithQuery("userId", "1")
            .WithQuery("_limit", "5")
            .GetAsync<Post[]>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public async Task RequestBuilder_PostAsync_SendsData()
    {
        // Arrange
        var newPost = new
        {
            title = "Test Post",
            body = "This is a test",
            userId = 1
        };

        // Act
        var response = await _nexar.Request()
            .Url("/posts")
            .PostAsync<object, Post>(newPost);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Equal(201, response.Status);
    }

    [Fact]
    public async Task RequestBuilder_PutAsync_UpdatesData()
    {
        // Arrange
        var updatedPost = new
        {
            id = 1,
            title = "Updated Post",
            body = "This is updated",
            userId = 1
        };

        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .PutAsync<object, Post>(updatedPost);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task RequestBuilder_DeleteAsync_DeletesResource()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .DeleteAsync<object>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task RequestBuilder_WithBearerToken_AddsAuthHeader()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .WithBearerToken("test-token-123")
            .GetAsync<Post>();

        // Assert
        Assert.NotNull(response);
        // The request will succeed even with invalid token for this test API
    }

    [Fact]
    public async Task RequestBuilder_WithBasicAuth_AddsAuthHeader()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .WithBasicAuth("username", "password")
            .GetAsync<Post>();

        // Assert
        Assert.NotNull(response);
        // The request will succeed even with invalid credentials for this test API
    }

    [Fact]
    public async Task RequestBuilder_WithApiKey_AddsCustomHeader()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts/1")
            .WithApiKey("X-API-Key", "test-api-key")
            .GetAsync<Post>();

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task RequestBuilder_ChainedMethods_WorksCorrectly()
    {
        // Act
        var response = await _nexar.Request()
            .Url("/posts")
            .WithHeader("Accept", "application/json")
            .WithHeader("User-Agent", "Nexar-Test/1.0")
            .WithQuery("userId", "1")
            .WithQuery("_limit", "3")
            .GetAsync<Post[]>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public void RequestBuilder_ReturnsNewInstance()
    {
        // Act
        var builder1 = _nexar.Request();
        var builder2 = _nexar.Request();

        // Assert
        Assert.NotNull(builder1);
        Assert.NotNull(builder2);
        Assert.NotSame(builder1, builder2);
    }

    [Fact]
    public async Task RequestBuilder_WithContentType_FormUrlEncoded()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        var response = await _nexar.Request()
            .Url("https://httpbin.org/post")
            .WithBody(formData)
            .WithContentType(ContentType.FormUrlEncoded)
            .PostAsync<string>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("application/x-www-form-urlencoded", response.RawContent);
    }

    [Fact]
    public async Task RequestBuilder_WithContentType_FormData()
    {
        // Arrange
        var formData = new Dictionary<string, object>
        {
            { "field", "value" },
            { "file", System.Text.Encoding.UTF8.GetBytes("content") }
        };

        // Act
        var response = await _nexar.Request()
            .Url("https://httpbin.org/post")
            .WithBody(formData)
            .WithContentType(ContentType.FormData)
            .PostAsync<string>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Contains("multipart/form-data", response.RawContent);
    }

    [Fact]
    public async Task RequestBuilder_WithContentType_Binary()
    {
        // Arrange
        var binaryData = System.Text.Encoding.UTF8.GetBytes("Binary test data");

        // Act
        var response = await _nexar.Request()
            .Url("https://httpbin.org/post")
            .WithBody(binaryData)
            .WithContentType(ContentType.Binary)
            .PostAsync<string>();

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
    }

    public void Dispose()
    {
        _nexar?.Dispose();
    }

    // Helper class for testing
    private class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public int UserId { get; set; }
    }
}
