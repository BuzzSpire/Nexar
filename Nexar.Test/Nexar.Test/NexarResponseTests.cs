using System.Net;
using Nexar.Models;

namespace Nexar.Test;

/// <summary>
/// Unit tests for the NexarResponse&lt;T&gt; model class.
/// </summary>
public class NexarResponseTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var response = new NexarResponse<string>();

        Assert.Null(response.Data);
        Assert.Equal(0, response.Status);
        Assert.Equal(default(HttpStatusCode), response.StatusCode);
        Assert.Equal(string.Empty, response.StatusText);
        Assert.NotNull(response.Headers);
        Assert.Empty(response.Headers);
        Assert.Equal(string.Empty, response.RawContent);
        Assert.False(response.IsSuccess);
        Assert.Null(response.ErrorMessage);
        Assert.Null(response.Exception);
        Assert.Null(response.Config);
    }

    [Fact]
    public void Data_CanBeSet()
    {
        var response = new NexarResponse<string> { Data = "hello" };
        Assert.Equal("hello", response.Data);
    }

    [Fact]
    public void Data_WithGenericType_CanBeSet()
    {
        var obj = new { Id = 1, Name = "test" };
        var response = new NexarResponse<object> { Data = obj };
        Assert.Equal(obj, response.Data);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void Status_CanBeSet(int statusCode)
    {
        var response = new NexarResponse<string> { Status = statusCode };
        Assert.Equal(statusCode, response.Status);
    }

    [Fact]
    public void StatusCode_CanBeSet()
    {
        var response = new NexarResponse<string> { StatusCode = HttpStatusCode.NotFound };
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void StatusText_CanBeSet()
    {
        var response = new NexarResponse<string> { StatusText = "OK" };
        Assert.Equal("OK", response.StatusText);
    }

    [Fact]
    public void Headers_CanBeSet()
    {
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            { "Content-Type", new[] { "application/json" } },
            { "X-Custom", new[] { "value1", "value2" } }
        };

        var response = new NexarResponse<string> { Headers = headers };

        Assert.Equal(2, response.Headers.Count);
        Assert.Contains("Content-Type", response.Headers.Keys);
        Assert.Contains("application/json", response.Headers["Content-Type"]);
    }

    [Fact]
    public void RawContent_CanBeSet()
    {
        const string raw = "{\"id\":1,\"title\":\"test\"}";
        var response = new NexarResponse<string> { RawContent = raw };
        Assert.Equal(raw, response.RawContent);
    }

    [Fact]
    public void IsSuccess_CanBeSetToTrue()
    {
        var response = new NexarResponse<string> { IsSuccess = true };
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void IsSuccess_CanBeSetToFalse()
    {
        var response = new NexarResponse<string> { IsSuccess = false };
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void ErrorMessage_CanBeSet()
    {
        const string msg = "Request failed with status code 404";
        var response = new NexarResponse<string> { ErrorMessage = msg };
        Assert.Equal(msg, response.ErrorMessage);
    }

    [Fact]
    public void Exception_CanBeSet()
    {
        var ex = new InvalidOperationException("timeout");
        var response = new NexarResponse<string> { Exception = ex };

        Assert.NotNull(response.Exception);
        Assert.Equal("timeout", response.Exception.Message);
    }

    [Fact]
    public void Config_CanBeSet()
    {
        var options = new RequestOptions { Url = "https://example.com", Method = "GET" };
        var response = new NexarResponse<string> { Config = options };

        Assert.NotNull(response.Config);
        Assert.Equal("https://example.com", response.Config.Url);
    }

    [Fact]
    public void SuccessResponse_ObjectInitializer_AllPropertiesSet()
    {
        var response = new NexarResponse<int>
        {
            Data = 42,
            Status = 200,
            StatusCode = HttpStatusCode.OK,
            StatusText = "OK",
            RawContent = "42",
            IsSuccess = true,
            Headers = new Dictionary<string, IEnumerable<string>>
            {
                { "Content-Type", new[] { "application/json" } }
            }
        };

        Assert.Equal(42, response.Data);
        Assert.Equal(200, response.Status);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.StatusText);
        Assert.Equal("42", response.RawContent);
        Assert.True(response.IsSuccess);
        Assert.Single(response.Headers);
    }

    [Fact]
    public void FailureResponse_ObjectInitializer_AllPropertiesSet()
    {
        var ex = new HttpRequestException("Connection refused");
        var response = new NexarResponse<string>
        {
            IsSuccess = false,
            Status = 500,
            StatusCode = HttpStatusCode.InternalServerError,
            StatusText = "Internal Server Error",
            ErrorMessage = "Connection refused",
            Exception = ex
        };

        Assert.False(response.IsSuccess);
        Assert.Equal(500, response.Status);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(response.ErrorMessage);
        Assert.NotNull(response.Exception);
        Assert.IsType<HttpRequestException>(response.Exception);
    }

    [Fact]
    public void NexarResponse_WithNullableGenericType_AcceptsNull()
    {
        var response = new NexarResponse<string?> { Data = null };
        Assert.Null(response.Data);
    }

    [Fact]
    public void NexarResponse_WithComplexType_StoresCorrectly()
    {
        var data = new List<int> { 1, 2, 3 };
        var response = new NexarResponse<List<int>> { Data = data };

        Assert.NotNull(response.Data);
        Assert.Equal(3, response.Data.Count);
        Assert.Equal(new[] { 1, 2, 3 }, response.Data);
    }
}
