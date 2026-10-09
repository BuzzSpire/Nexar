using System.Net;
using Nexar.Testing;

namespace Nexar.Test;

public class CircuitBreakerTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (MockHttp Mock, NexarClient Client, ManualClock Clock) Setup(Func<MockRequest, HttpStatusCode> status)
    {
        var clock = new ManualClock();
        var mock = new MockHttp();
        mock.OnAny().RespondWith(r => new HttpResponseMessage(status(r)) { Content = new StringContent("") });
        var client = mock.CreateClient("https://a.test", b => b.CircuitBreaker(
            failureRatio: 0.5, minimumThroughput: 4, samplingDuration: TimeSpan.FromSeconds(30), breakDuration: TimeSpan.FromSeconds(10), clock: clock));
        return (mock, client, clock);
    }

    private static async Task<HttpStatusCode> Call(NexarClient client, string url = "/")
    {
        using var res = await client.Get(url).Send();
        return res.StatusCode;
    }

    [Fact]
    public async Task OpensAfterTooManyFailuresAndFailsFast()
    {
        var (mock, client, _) = Setup(_ => HttpStatusCode.ServiceUnavailable);
        using var _ = client;

        for (var i = 0; i < 4; i++)
        {
            await Call(client);
        }
        var ex = await Assert.ThrowsAsync<NexarException>(() => Call(client));

        Assert.True(ex.IsCircuitOpen);
        Assert.Equal(TimeSpan.FromSeconds(10), ex.RetryAfter);
        Assert.Equal(4, mock.Requests.Count);   // the rejected call never reached the server
    }

    [Fact]
    public async Task StaysClosedBelowTheThresholds()
    {
        var calls = 0;
        var (mock, client, _) = Setup(_ => ++calls % 3 == 0 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);   // 33% failures
        using var _ = client;

        for (var i = 0; i < 12; i++)
        {
            await Call(client);
        }

        Assert.Equal(12, mock.Requests.Count);
    }

    [Fact]
    public async Task ClientErrorsAreNotFailures()
    {
        var (mock, client, _) = Setup(_ => HttpStatusCode.NotFound);
        using var _ = client;

        for (var i = 0; i < 10; i++)
        {
            await Call(client);
        }

        Assert.Equal(10, mock.Requests.Count);
    }

    [Fact]
    public async Task HalfOpenProbeSuccessCloses()
    {
        var healthy = false;
        var (mock, client, clock) = Setup(_ => healthy ? HttpStatusCode.OK : HttpStatusCode.BadGateway);
        using var _ = client;
        for (var i = 0; i < 4; i++)
        {
            await Call(client);
        }

        clock.Advance(TimeSpan.FromSeconds(11));
        healthy = true;
        var probe = await Call(client);
        var after = await Call(client);

        Assert.Equal(HttpStatusCode.OK, probe);
        Assert.Equal(HttpStatusCode.OK, after);
        Assert.Equal(6, mock.Requests.Count);
    }

    [Fact]
    public async Task HalfOpenProbeFailureOpensAgain()
    {
        var (mock, client, clock) = Setup(_ => HttpStatusCode.ServiceUnavailable);
        using var _ = client;
        for (var i = 0; i < 4; i++)
        {
            await Call(client);
        }

        clock.Advance(TimeSpan.FromSeconds(11));
        await Call(client);   // the probe fails
        var ex = await Assert.ThrowsAsync<NexarException>(() => Call(client));

        Assert.True(ex.IsCircuitOpen);
        Assert.Equal(5, mock.Requests.Count);
    }

    [Fact]
    public async Task TransportFailuresCount()
    {
        var clock = new ManualClock();
        var mock = new MockHttp();
        mock.OnAny().Throws(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using var client = mock.CreateClient(configure: b => b.CircuitBreaker(minimumThroughput: 2, clock: clock));

        await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());
        await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());
        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        Assert.True(ex.IsCircuitOpen);
    }

    [Fact]
    public async Task HostsAreIsolated()
    {
        var (mock, client, _) = Setup(r => r.Url.Host == "a.test" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        using var _ = client;
        for (var i = 0; i < 4; i++)
        {
            await Call(client);
        }

        var other = await Call(client, "https://b.test/");

        Assert.Equal(HttpStatusCode.OK, other);
        await Assert.ThrowsAsync<NexarException>(() => Call(client));
    }

    [Fact]
    public async Task OldFailuresLeaveTheSamplingWindow()
    {
        var (mock, client, clock) = Setup(_ => HttpStatusCode.ServiceUnavailable);
        using var _ = client;

        for (var i = 0; i < 3; i++)
        {
            await Call(client);
        }
        clock.Advance(TimeSpan.FromSeconds(31));
        await Call(client);   // only 1 sample in the window now
        await Call(client);

        Assert.Equal(5, mock.Requests.Count);
    }

    [Fact]
    public async Task DerivedClientsShareTheCircuit()
    {
        var (mock, client, _) = Setup(_ => HttpStatusCode.ServiceUnavailable);
        using var _ = client;
        using var derived = client.With(b => b.DefaultHeader("X", "1"));

        for (var i = 0; i < 4; i++)
        {
            await Call(client);
        }

        await Assert.ThrowsAsync<NexarException>(() => Call(derived));
    }

    [Fact]
    public void InvalidSettingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().CircuitBreaker(failureRatio: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().CircuitBreaker(minimumThroughput: 0));
    }
}
