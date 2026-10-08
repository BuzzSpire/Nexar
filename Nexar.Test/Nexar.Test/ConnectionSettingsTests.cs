using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Nexar.Test;

public class ConnectionSettingsTests
{
    [Fact]
    public async Task ConnectTimeoutFailsFastWhenTheHandshakeStalls()
    {
        // Accepts TCP connections but never answers the TLS handshake.
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(8);
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var client = NexarClient.Builder()
            .ConnectTimeout(TimeSpan.FromMilliseconds(300))
            .Timeout(TimeSpan.FromSeconds(30))
            .Build();

        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<NexarException>(() => client.Get($"https://127.0.0.1:{port}/").Send());

        Assert.True(ex.IsTimeout, $"Expected a timeout, got {ex.Kind}: {ex.Message}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}");
    }

    [Fact]
    public void PoolSettingsReachTheHandler()
    {
        using var handler = NexarClient.Builder()
            .ConnectTimeout(TimeSpan.FromSeconds(3))
            .PoolIdleTimeout(TimeSpan.FromSeconds(90))
            .PoolConnectionLifetime(TimeSpan.FromMinutes(5))
            .MaxConnectionsPerHost(20)
            .CreateDefaultHandler();

        Assert.Equal(TimeSpan.FromSeconds(3), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(90), handler.PooledConnectionIdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
        Assert.Equal(20, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void InvalidValuesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().ConnectTimeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().PoolIdleTimeout(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NexarClient.Builder().MaxConnectionsPerHost(0));
    }
}
