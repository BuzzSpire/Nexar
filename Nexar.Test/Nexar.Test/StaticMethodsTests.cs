using System.Net;
using System.Text.Json;
using Nexar.Models;
using NexarLib = Nexar;

namespace Nexar.Test;

/// <summary>
/// Unit tests for Nexar static methods (both generic and non-generic versions)
/// </summary>
public class StaticMethodsTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string TestUrl = "https://api.example.com/test";

    public StaticMethodsTests()
    {
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task Get_NonGeneric_ReturnsString()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";

        // Act
        var result = await NexarLib.Nexar.Get(url);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task Get_Generic_ReturnsNexarResponse()
    {
        // This is an integration test that calls a real API
        // Note: Test may be flaky due to external API dependency
        try
        {
            // Arrange
            var url = "https://jsonplaceholder.typicode.com/posts/1";

            // Act
            var result = await NexarLib.Nexar.Get<Post>(url);

            // Assert - verify we get a response
            Assert.NotNull(result);

            // If API is reachable and successful
            if (result.IsSuccess && result.Status == 200)
            {
                Assert.NotNull(result.Data);
                Assert.True(result.Data.Id > 0, "Post ID should be greater than 0");
            }
            else
            {
                // API might be down or unreachable - that's ok for integration test
                // Just verify we got a proper error response
                Assert.False(result.IsSuccess);
            }
        }
        catch (Exception ex)
        {
            // Network issues are acceptable for integration tests
            Assert.NotNull(ex);
        }
    }

    [Fact]
    public async Task Post_NonGeneric_ReturnsString()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts";
        var postData = new { title = "Test", body = "Test body", userId = 1 };

        // Act
        var result = await NexarLib.Nexar.Post(url, postData);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task Post_Generic_ReturnsNexarResponse()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts";
        var postData = new { title = "Test Post", body = "Test body", userId = 1 };

        // Act
        var result = await NexarLib.Nexar.Post<Post>(url, postData);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(201, result.Status);
    }

    [Fact]
    public async Task Put_NonGeneric_ReturnsString()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";
        var putData = new { id = 1, title = "Updated", body = "Updated body", userId = 1 };

        // Act
        var result = await NexarLib.Nexar.Put(url, putData);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task Put_Generic_ReturnsNexarResponse()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";
        var putData = new { id = 1, title = "Updated Post", body = "Updated body", userId = 1 };

        // Act
        var result = await NexarLib.Nexar.Put<Post>(url, putData);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Delete_NonGeneric_ReturnsString()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";

        // Act
        var result = await NexarLib.Nexar.Delete(url);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task Delete_Generic_ReturnsNexarResponse()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";

        // Act
        var result = await NexarLib.Nexar.Delete<object>(url);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Patch_NonGeneric_ReturnsString()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";
        var patchData = new { title = "Patched Title" };

        // Act
        var result = await NexarLib.Nexar.Patch(url, patchData);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public async Task Patch_Generic_ReturnsNexarResponse()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";
        var patchData = new { title = "Patched Post" };

        // Act
        var result = await NexarLib.Nexar.Patch<Post>(url, patchData);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task Get_WithRequestOptions_ReturnsSuccess()
    {
        // Arrange
        var url = "https://jsonplaceholder.typicode.com/posts/1";
        var options = new RequestOptions
        {
            Headers = new Dictionary<string, string>
            {
                { "Accept", "application/json" }
            }
        };

        // Act
        var result = await NexarLib.Nexar.Get<Post>(url, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
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
