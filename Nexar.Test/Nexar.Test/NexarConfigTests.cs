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
        Assert.Equal(100, config.TimeoutSeconds);
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
    public void TimeoutSeconds_CanBeSet()
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.TimeoutSeconds = 30;

        // Assert
        Assert.Equal(30, config.TimeoutSeconds);
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
            TimeoutSeconds = 60,
            MaxRetryAttempts = 5,
            RetryDelayMilliseconds = 500,
            UseExponentialBackoff = false,
            ValidateSslCertificates = false
        };

        // Assert
        Assert.Equal("https://api.example.com", config.BaseUrl);
        Assert.Single(config.DefaultHeaders);
        Assert.Equal(60, config.TimeoutSeconds);
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
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(300)]
    public void TimeoutSeconds_WithVariousValues_SetsCorrectly(int timeout)
    {
        // Arrange
        var config = new NexarConfig();

        // Act
        config.TimeoutSeconds = timeout;

        // Assert
        Assert.Equal(timeout, config.TimeoutSeconds);
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
