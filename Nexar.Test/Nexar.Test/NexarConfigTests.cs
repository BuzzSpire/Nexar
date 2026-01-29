using Nexar.Configuration;

namespace Nexar.Test;

/// <summary>
/// Unit tests for NexarConfig
/// </summary>
public class NexarConfigTests
{
    [Fact]
    public void Constructor_DefaultValues_AreSetCorrectly()
    {
        // Act
        var config = new NexarConfig();

        // Assert
        Assert.Null(config.BaseUrl);
        Assert.NotNull(config.DefaultHeaders);
        Assert.Empty(config.DefaultHeaders);
        Assert.Equal(100_000, config.TimeoutMs);
        Assert.Equal(0, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.True(config.UseExponentialBackoff);
        Assert.True(config.ValidateSslCertificates);
    }

    [Fact]
    public void BaseUrl_CanBeSet()
    {
        // Arrange
        var config = new NexarConfig();
        var baseUrl = "https://api.example.com";

        // Act
        config.BaseUrl = baseUrl;

        // Assert
        Assert.Equal(baseUrl, config.BaseUrl);
    }

    [Fact]
    public void DefaultHeaders_CanBeModified()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.DefaultHeaders.Add("Accept", "application/json");
        config.DefaultHeaders.Add("User-Agent", "Nexar/1.0");

        // Assert
        Assert.Equal(2, config.DefaultHeaders.Count);
        Assert.Equal("application/json", config.DefaultHeaders["Accept"]);
        Assert.Equal("Nexar/1.0", config.DefaultHeaders["User-Agent"]);
    }

    [Fact]
    public void TimeoutMs_CanBeSet()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.TimeoutMs = 30_000;

        // Assert
        Assert.Equal(30_000, config.TimeoutMs);
    }

    [Fact]
    public void MaxRetryAttempts_CanBeSet()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.MaxRetryAttempts = 3;

        // Assert
        Assert.Equal(3, config.MaxRetryAttempts);
    }

    [Fact]
    public void RetryDelayMilliseconds_CanBeSet()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.RetryDelayMilliseconds = 2000;

        // Assert
        Assert.Equal(2000, config.RetryDelayMilliseconds);
    }

    [Fact]
    public void UseExponentialBackoff_CanBeToggled()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.UseExponentialBackoff = false;

        // Assert
        Assert.False(config.UseExponentialBackoff);
    }

    [Fact]
    public void ValidateSslCertificates_CanBeToggled()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.ValidateSslCertificates = false;

        // Assert
        Assert.False(config.ValidateSslCertificates);
    }

    [Fact]
    public void ObjectInitializer_SetsAllProperties()
    {
        // Act
        var config = new NexarConfig
        {
            BaseUrl = "https://api.example.com",
            DefaultHeaders = new Dictionary<string, string>
            {
                { "Accept", "application/json" }
            },
            TimeoutMs = 60_000,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 500,
            UseExponentialBackoff = false,
            ValidateSslCertificates = false
        };

        // Assert
        Assert.Equal("https://api.example.com", config.BaseUrl);
        Assert.Single(config.DefaultHeaders);
        Assert.Equal(60_000, config.TimeoutMs);
        Assert.Equal(5, config.MaxRetryAttempts);
        Assert.Equal(500, config.RetryDelayMilliseconds);
        Assert.False(config.UseExponentialBackoff);
        Assert.False(config.ValidateSslCertificates);
    }

    [Fact]
    public void DefaultHeaders_WithMultipleHeaders_StoresCorrectly()
    {
        // Arrange
        var config = new NexarConfig
        {
            DefaultHeaders = new Dictionary<string, string>
            {
                { "Accept", "application/json" },
                { "Content-Type", "application/json" },
                { "Authorization", "Bearer token123" },
                { "X-Custom-Header", "custom-value" }
            }
        };

        // Assert
        Assert.Equal(4, config.DefaultHeaders.Count);
        Assert.Equal("application/json", config.DefaultHeaders["Accept"]);
        Assert.Equal("Bearer token123", config.DefaultHeaders["Authorization"]);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(30_000)]
    [InlineData(100_000)]
    [InlineData(300_000)]
    public void TimeoutMs_WithVariousValues_SetsCorrectly(int timeout)
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.TimeoutMs = timeout;

        // Assert
        Assert.Equal(timeout, config.TimeoutMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void MaxRetryAttempts_WithVariousValues_SetsCorrectly(int retries)
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.MaxRetryAttempts = retries;

        // Assert
        Assert.Equal(retries, config.MaxRetryAttempts);
    }
}
