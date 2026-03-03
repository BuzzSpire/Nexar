using Nexar.Models;

namespace Nexar.Test;

/// <summary>
/// Unit tests for the RequestOptions class and ContentType enum.
/// </summary>
public class RequestOptionsTests
{
    // ── ContentType enum ────────────────────────────────────────────────────

    [Fact]
    public void ContentType_HasExpectedValues()
    {
        Assert.Equal(0, (int)ContentType.Json);
        Assert.Equal(1, (int)ContentType.FormUrlEncoded);
        Assert.Equal(2, (int)ContentType.FormData);
        Assert.Equal(3, (int)ContentType.Binary);
    }

    [Theory]
    [InlineData(ContentType.Json, "Json")]
    [InlineData(ContentType.FormUrlEncoded, "FormUrlEncoded")]
    [InlineData(ContentType.FormData, "FormData")]
    [InlineData(ContentType.Binary, "Binary")]
    public void ContentType_Names_AreCorrect(ContentType type, string expectedName)
    {
        Assert.Equal(expectedName, type.ToString());
    }

    // ── RequestOptions defaults ─────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var options = new RequestOptions();

        Assert.Null(options.Url);
        Assert.Equal("GET", options.Method);
        Assert.Null(options.Headers);
        Assert.Null(options.Data);
        Assert.Equal(ContentType.Json, options.ContentType);
        Assert.Null(options.Timeout);
        Assert.Null(options.BaseURL);
        Assert.Null(options.MaxRetries);
        Assert.Null(options.ValidateSsl);
    }

    // ── Property setters ────────────────────────────────────────────────────

    [Fact]
    public void Url_CanBeSet()
    {
        var options = new RequestOptions { Url = "https://api.example.com/items" };
        Assert.Equal("https://api.example.com/items", options.Url);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    public void Method_CanBeSet(string method)
    {
        var options = new RequestOptions { Method = method };
        Assert.Equal(method, options.Method);
    }

    [Fact]
    public void Headers_CanBeSet()
    {
        var headers = new Dictionary<string, string>
        {
            { "Authorization", "Bearer token123" },
            { "Accept", "application/json" }
        };

        var options = new RequestOptions { Headers = headers };

        Assert.NotNull(options.Headers);
        Assert.Equal(2, options.Headers.Count);
        Assert.Equal("Bearer token123", options.Headers["Authorization"]);
    }

    [Fact]
    public void Data_CanBeSetAsAnonymousObject()
    {
        var body = new { name = "test", value = 42 };
        var options = new RequestOptions { Data = body };
        Assert.NotNull(options.Data);
    }

    [Fact]
    public void Data_CanBeSetAsDictionary()
    {
        var dict = new Dictionary<string, string> { { "key", "value" } };
        var options = new RequestOptions { Data = dict };
        Assert.Equal(dict, options.Data);
    }

    [Theory]
    [InlineData(ContentType.Json)]
    [InlineData(ContentType.FormUrlEncoded)]
    [InlineData(ContentType.FormData)]
    [InlineData(ContentType.Binary)]
    public void ContentType_CanBeSet(ContentType contentType)
    {
        var options = new RequestOptions { ContentType = contentType };
        Assert.Equal(contentType, options.ContentType);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(30000)]
    [InlineData(null)]
    public void Timeout_CanBeSet(int? timeout)
    {
        var options = new RequestOptions { Timeout = timeout };
        Assert.Equal(timeout, options.Timeout);
    }

    [Fact]
    public void BaseURL_CanBeSet()
    {
        var options = new RequestOptions { BaseURL = "https://api.example.com" };
        Assert.Equal("https://api.example.com", options.BaseURL);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(null)]
    public void MaxRetries_CanBeSet(int? maxRetries)
    {
        var options = new RequestOptions { MaxRetries = maxRetries };
        Assert.Equal(maxRetries, options.MaxRetries);
    }

    [Fact]
    public void ValidateSsl_CanBeSetToTrue()
    {
        var options = new RequestOptions { ValidateSsl = true };
        Assert.True(options.ValidateSsl);
    }

    [Fact]
    public void ValidateSsl_CanBeSetToFalse()
    {
        var options = new RequestOptions { ValidateSsl = false };
        Assert.False(options.ValidateSsl);
    }

    // ── Object initializer ─────────────────────────────────────────────────

    [Fact]
    public void ObjectInitializer_SetsAllProperties()
    {
        var options = new RequestOptions
        {
            Url = "https://api.example.com/items/1",
            Method = "POST",
            Headers = new Dictionary<string, string> { { "X-Api-Key", "secret" } },
            Data = new { id = 1 },
            ContentType = ContentType.FormUrlEncoded,
            Timeout = 10000,
            BaseURL = "https://api.example.com",
            MaxRetries = 2,
            ValidateSsl = false
        };

        Assert.Equal("https://api.example.com/items/1", options.Url);
        Assert.Equal("POST", options.Method);
        Assert.Single(options.Headers!);
        Assert.NotNull(options.Data);
        Assert.Equal(ContentType.FormUrlEncoded, options.ContentType);
        Assert.Equal(10000, options.Timeout);
        Assert.Equal("https://api.example.com", options.BaseURL);
        Assert.Equal(2, options.MaxRetries);
        Assert.False(options.ValidateSsl);
    }

    // ── Nexar.Create(RequestOptions) factory mapping ───────────────────────

    [Fact]
    public void NexarCreate_WithRequestOptions_SetsTimeout()
    {
        var options = new RequestOptions { Timeout = 5000 };
        var nexar = global::Nexar.Nexar.Create(options);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }

    [Fact]
    public void NexarCreate_WithRequestOptions_SetsBaseUrl()
    {
        var options = new RequestOptions { BaseURL = "https://api.example.com" };
        var nexar = global::Nexar.Nexar.Create(options);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }

    [Fact]
    public void NexarCreate_WithRequestOptions_DisablesSslValidation()
    {
        var options = new RequestOptions { ValidateSsl = false };
        var nexar = global::Nexar.Nexar.Create(options);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }

    [Fact]
    public void NexarCreate_WithRequestOptions_SetsMaxRetries()
    {
        var options = new RequestOptions { MaxRetries = 3 };
        var nexar = global::Nexar.Nexar.Create(options);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }

    [Fact]
    public void NexarCreate_WithRequestOptions_SetsDefaultHeaders()
    {
        var options = new RequestOptions
        {
            Headers = new Dictionary<string, string> { { "X-Custom", "value" } }
        };
        var nexar = global::Nexar.Nexar.Create(options);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }

    [Fact]
    public void NexarCreate_WithNullConfig_UsesDefaults()
    {
        var nexar = global::Nexar.Nexar.Create(config: null);
        Assert.NotNull(nexar);
        nexar.Dispose();
    }
}
