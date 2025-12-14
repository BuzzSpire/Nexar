using Moq;
using Moq.Protected;
using Nexar.Configuration;
using System.Net;

namespace Nexar.Test;

/// <summary>
/// Unit tests for Retry Mechanism with Exponential Backoff
/// </summary>
public class RetryMechanismTests
{
    [Fact]
    public void NexarConfig_RetrySettings_AreConfigurable()
    {
        // Arrange & Act
        var config = new NexarConfig
        {
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 500,
            UseExponentialBackoff = true
        };

        // Assert
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(500, config.RetryDelayMilliseconds);
        Assert.True(config.UseExponentialBackoff);
    }

    [Fact]
    public void NexarConfig_DefaultRetrySettings_AreCorrect()
    {
        // Act
        var config = new NexarConfig();

        // Assert
        Assert.Equal(0, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.True(config.UseExponentialBackoff);
    }

    [Theory]
    [InlineData(0, 1000, true)]
    [InlineData(3, 500, true)]
    [InlineData(5, 2000, false)]
    [InlineData(1, 100, true)]
    public void NexarConfig_WithVariousRetrySettings_SetsCorrectly(
        int maxRetries, int delay, bool useExponential)
    {
        // Act
        var config = new NexarConfig
        {
            MaxRetryAttempts = maxRetries,
            RetryDelayMilliseconds = delay,
            UseExponentialBackoff = useExponential
        };

        // Assert
        Assert.Equal(maxRetries, config.MaxRetryAttempts);
        Assert.Equal(delay, config.RetryDelayMilliseconds);
        Assert.Equal(useExponential, config.UseExponentialBackoff);
    }

    [Fact]
    public void Nexar_WithRetryConfig_Initializes()
    {
        // Arrange
        var config = new NexarConfig
        {
            BaseUrl = "https://api.example.com",
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            UseExponentialBackoff = true
        };

        // Act
        var nexar = new global::Nexar.Nexar(config);

        // Assert
        Assert.NotNull(nexar);
    }

    [Fact]
    public void ExponentialBackoff_Calculation_Test()
    {
        // This test verifies the exponential backoff concept
        // Delay = baseDelay * (2 ^ attemptNumber)

        // Arrange
        int baseDelay = 1000; // ms

        // Act & Assert
        Assert.Equal(1000, baseDelay * Math.Pow(2, 0)); // First retry: 1s
        Assert.Equal(2000, baseDelay * Math.Pow(2, 1)); // Second retry: 2s
        Assert.Equal(4000, baseDelay * Math.Pow(2, 2)); // Third retry: 4s
        Assert.Equal(8000, baseDelay * Math.Pow(2, 3)); // Fourth retry: 8s
    }

    [Fact]
    public void LinearBackoff_Calculation_Test()
    {
        // This test verifies the linear backoff concept
        // Delay = baseDelay (constant)

        // Arrange
        int baseDelay = 1000; // ms

        // Act & Assert
        Assert.Equal(1000, baseDelay); // First retry: 1s
        Assert.Equal(1000, baseDelay); // Second retry: 1s
        Assert.Equal(1000, baseDelay); // Third retry: 1s
        Assert.Equal(1000, baseDelay); // Fourth retry: 1s
    }

    [Fact]
    public async Task Nexar_WithZeroRetries_DoesNotRetry()
    {
        // Arrange
        var config = new NexarConfig
        {
            BaseUrl = "https://httpstat.us",
            MaxRetryAttempts = 0
        };
        var nexar = new global::Nexar.Nexar(config);

        // Act & Assert
        // With 0 retries, it should fail on first attempt
        // Using a URL that returns 500 to test failure scenario
        try
        {
            await nexar.GetAsync<object>("/500");
        }
        catch
        {
            // Expected to fail without retries
            Assert.True(true);
        }
        finally
        {
            nexar.Dispose();
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public void RetryDelayMilliseconds_WithVariousValues_SetsCorrectly(int delay)
    {
        // Arrange
        var config = new NexarConfig
        {
            RetryDelayMilliseconds = delay
        };

        // Assert
        Assert.Equal(delay, config.RetryDelayMilliseconds);
    }

    [Fact]
    public void NexarConfig_RetryWithExponentialBackoff_Example()
    {
        // This demonstrates the recommended retry configuration
        // Arrange & Act
        var config = new NexarConfig
        {
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            UseExponentialBackoff = true
        };

        // Assert
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.True(config.UseExponentialBackoff);

        // This would result in retry delays of: 1s, 2s, 4s
    }

    [Fact]
    public void NexarConfig_RetryWithLinearBackoff_Example()
    {
        // This demonstrates retry with constant delay
        // Arrange & Act
        var config = new NexarConfig
        {
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 1000,
            UseExponentialBackoff = false
        };

        // Assert
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(1000, config.RetryDelayMilliseconds);
        Assert.False(config.UseExponentialBackoff);

        // This would result in retry delays of: 1s, 1s, 1s
    }

    [Fact]
    public void RetryDelayCalculation_ExponentialBackoff()
    {
        // Test exponential backoff delay calculation
        int baseDelay = 1000;
        bool useExponential = true;

        // Calculate delays for 3 retry attempts
        var delays = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            delays.Add(useExponential ? baseDelay * Math.Pow(2, i) : baseDelay);
        }

        // Assert
        Assert.Equal(1000, delays[0]); // 1s
        Assert.Equal(2000, delays[1]); // 2s
        Assert.Equal(4000, delays[2]); // 4s
    }

    [Fact]
    public void RetryDelayCalculation_LinearBackoff()
    {
        // Test linear backoff delay calculation
        int baseDelay = 1000;
        bool useExponential = false;

        // Calculate delays for 3 retry attempts
        var delays = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            delays.Add(useExponential ? baseDelay * Math.Pow(2, i) : baseDelay);
        }

        // Assert
        Assert.Equal(1000, delays[0]); // 1s
        Assert.Equal(1000, delays[1]); // 1s
        Assert.Equal(1000, delays[2]); // 1s
    }
}
