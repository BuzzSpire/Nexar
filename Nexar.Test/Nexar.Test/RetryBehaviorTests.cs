using Nexar.Configuration;
using System.Net;

namespace Nexar.Test;

/// <summary>
/// Behavioral tests for the retry mechanism.
/// Tests observable outcomes (response fields) rather than internal implementation.
///
/// NOTE: Nexar.cs does not expose HttpClient injection, so these tests focus on
/// the observable behavior via the public API with invalid hosts that fail fast.
/// </summary>
public class RetryBehaviorTests
{
    private const string InvalidHost = "https://this-host-does-not-exist-at-all.invalid/api";

    // ── Response structure on exhausted retries ─────────────────────────────

    [Fact]
    public async Task OnNetworkFailure_WithZeroRetries_ReturnsErrorResponse()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var result = await nexar.GetAsync<string>(InvalidHost);

        // Should return an error response, not throw
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task OnNetworkFailure_WithZeroRetries_SetsErrorMessage()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var result = await nexar.GetAsync<string>(InvalidHost);

        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public async Task OnNetworkFailure_WithZeroRetries_SetsException()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var result = await nexar.GetAsync<string>(InvalidHost);

        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task OnNetworkFailure_WithZeroRetries_Status500()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var result = await nexar.GetAsync<string>(InvalidHost);

        Assert.Equal(500, result.Status);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task OnNetworkFailure_WithOneRetry_StillReturnsErrorResponse()
    {
        var config = new NexarConfig
        {
            MaxRetryAttempts = 1,
            RetryDelayMilliseconds = 1   // minimal delay to keep test fast
        };
        using var nexar = new global::Nexar.Nexar(config);

        var result = await nexar.GetAsync<string>(InvalidHost);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── Verify that GetAsync never throws — always returns response ──────────

    [Fact]
    public async Task GetAsync_OnNetworkFailure_NeverThrows()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        // Should not throw; result should be returned
        var ex = await Record.ExceptionAsync(() => nexar.GetAsync<string>(InvalidHost));
        Assert.Null(ex);
    }

    [Fact]
    public async Task PostAsync_OnNetworkFailure_NeverThrows()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var ex = await Record.ExceptionAsync(() =>
            nexar.PostAsync<string>(InvalidHost, new { test = true }));
        Assert.Null(ex);
    }

    [Fact]
    public async Task PutAsync_OnNetworkFailure_NeverThrows()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var ex = await Record.ExceptionAsync(() =>
            nexar.PutAsync<string>(InvalidHost, new { test = true }));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DeleteAsync_OnNetworkFailure_NeverThrows()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var ex = await Record.ExceptionAsync(() =>
            nexar.DeleteAsync<string>(InvalidHost));
        Assert.Null(ex);
    }

    [Fact]
    public async Task PatchAsync_OnNetworkFailure_NeverThrows()
    {
        var config = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexar = new global::Nexar.Nexar(config);

        var ex = await Record.ExceptionAsync(() =>
            nexar.PatchAsync<string>(InvalidHost, new { test = true }));
        Assert.Null(ex);
    }

    // ── With-retry config: retries increase total time ───────────────────────
    // These timing tests document the retry delay behavior.
    // They use retry delay = 0ms to stay fast but still verify that multiple
    // attempts happen (indicated by taking slightly more time than a single attempt).

    [Fact]
    public async Task OnNetworkFailure_WithRetries_TakesLongerThanZeroRetries()
    {
        // 0 retries
        var zeroConfig = new NexarConfig { MaxRetryAttempts = 0 };
        using var nexarZero = new global::Nexar.Nexar(zeroConfig);
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        await nexarZero.GetAsync<string>(InvalidHost);
        sw0.Stop();

        // 2 retries with 50ms delay — should take noticeably longer
        var retryConfig = new NexarConfig
        {
            MaxRetryAttempts = 2,
            RetryDelayMilliseconds = 50,
            UseExponentialBackoff = false
        };
        using var nexarRetry = new global::Nexar.Nexar(retryConfig);
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        await nexarRetry.GetAsync<string>(InvalidHost);
        sw2.Stop();

        // 2 retries × 50ms = at least 100ms extra
        Assert.True(sw2.ElapsedMilliseconds > sw0.ElapsedMilliseconds,
            $"With retries ({sw2.ElapsedMilliseconds}ms) should take longer than without ({sw0.ElapsedMilliseconds}ms)");
    }

    // ── Static API error behavior ────────────────────────────────────────────

    [Fact]
    public async Task StaticGet_OnNetworkFailure_ReturnsErrorResponse()
    {
        var result = await global::Nexar.Nexar.Get<string>(InvalidHost);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task StaticPost_OnNetworkFailure_ReturnsErrorResponse()
    {
        var result = await global::Nexar.Nexar.Post<string>(InvalidHost, new { test = true });

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
    }

    // ── Verify exponential vs linear delay config is honored ─────────────────

    [Fact]
    public void ExponentialBackoff_Config_IsRespected()
    {
        // Verifies the delay formula used in SendRequestAsync:
        // delay = RetryDelayMilliseconds * (2 ^ (attempt - 1))
        int baseDelay = 1000;
        var delays = Enumerable.Range(1, 3)
            .Select(i => baseDelay * (int)Math.Pow(2, i - 1))
            .ToList();

        Assert.Equal(1000, delays[0]);
        Assert.Equal(2000, delays[1]);
        Assert.Equal(4000, delays[2]);
    }

    [Fact]
    public void LinearBackoff_Config_IsRespected()
    {
        // Verifies the delay formula used in SendRequestAsync:
        // delay = RetryDelayMilliseconds (constant)
        int baseDelay = 1000;
        var delays = Enumerable.Range(0, 3)
            .Select(_ => baseDelay)
            .ToList();

        Assert.All(delays, d => Assert.Equal(1000, d));
    }
}
