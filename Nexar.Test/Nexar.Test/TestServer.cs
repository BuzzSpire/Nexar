using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Nexar.Test;

/// <summary>
/// A tiny HTTP/1.1 server over TCP, a Unix domain socket or TLS, for tests that need a real
/// <see cref="SocketsHttpHandler"/> (redirects, proxies, cookies, TLS, custom connections).
/// Every response closes the connection.
/// </summary>
public sealed class TestServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly Func<ServerRequest, ServerResponse> _handler;
    private readonly X509Certificate2? _certificate;
    private readonly bool _requireClientCertificate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;

    private TestServer(Socket listener, Func<ServerRequest, ServerResponse> handler, X509Certificate2? certificate, bool requireClientCertificate)
    {
        _listener = listener;
        _handler = handler;
        _certificate = certificate;
        _requireClientCertificate = requireClientCertificate;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public ConcurrentQueue<ServerRequest> Requests { get; } = new();

    public int Port => ((IPEndPoint)_listener.LocalEndPoint!).Port;

    /// <summary>http://127.0.0.1:{port} (or https:// for TLS servers, with host "localhost").</summary>
    public string Url => _certificate == null ? $"http://127.0.0.1:{Port}" : $"https://localhost:{Port}";

    public static TestServer Start(Func<ServerRequest, ServerResponse> handler) =>
        new(Listen(new IPEndPoint(IPAddress.Loopback, 0), AddressFamily.InterNetwork, ProtocolType.Tcp), handler, null, false);

    public static TestServer StartUnix(string path, Func<ServerRequest, ServerResponse> handler) =>
        new(Listen(new UnixDomainSocketEndPoint(path), AddressFamily.Unix, ProtocolType.Unspecified), handler, null, false);

    public static TestServer StartTls(X509Certificate2 certificate, Func<ServerRequest, ServerResponse> handler, bool requireClientCertificate = false) =>
        new(Listen(new IPEndPoint(IPAddress.Loopback, 0), AddressFamily.InterNetwork, ProtocolType.Tcp), handler, certificate, requireClientCertificate);

    private static Socket Listen(EndPoint endPoint, AddressFamily family, ProtocolType protocol)
    {
        var socket = new Socket(family, SocketType.Stream, protocol);
        socket.Bind(endPoint);
        socket.Listen(64);
        return socket;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(Socket socket)
    {
        using var _ = socket;
        try
        {
            Stream stream = new NetworkStream(socket, ownsSocket: false);
            X509Certificate2? clientCertificate = null;
            if (_certificate != null)
            {
                var ssl = new SslStream(stream);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    ClientCertificateRequired = _requireClientCertificate,
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                });
                clientCertificate = ssl.RemoteCertificate is { } remote ? new X509Certificate2(remote) : null;
                if (_requireClientCertificate && clientCertificate == null)
                {
                    return;
                }
                stream = ssl;
            }
            await using (stream)
            {
                await ServeAsync(stream, clientCertificate);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (System.Security.Authentication.AuthenticationException)
        {
        }
    }

    public static async Task ServeAsync(Stream stream, X509Certificate2? clientCertificate, Func<ServerRequest, ServerResponse> handler,
        ConcurrentQueue<ServerRequest> requests)
    {
        var head = await ReadHeadAsync(stream);
        if (head == null)
        {
            return;
        }
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var colon = line.IndexOf(':');
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
        }

        var partial = new ServerRequest(requestLine[0], requestLine[1], requestLine[2], headers, [], clientCertificate);

        // Expect: 100-continue lets the handler reject before the body is sent.
        if (headers.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
        {
            var early = handler(partial);
            if (early.RejectBeforeBody)
            {
                requests.Enqueue(partial);
                await WriteResponseAsync(stream, early);
                return;
            }
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray());
        }

        var body = await ReadBodyAsync(stream, headers);
        var request = partial with { Body = body };
        requests.Enqueue(request);
        await WriteResponseAsync(stream, handler(request));
    }

    private Task ServeAsync(Stream stream, X509Certificate2? clientCertificate) => ServeAsync(stream, clientCertificate, _handler, Requests);

    private static async Task<string?> ReadHeadAsync(Stream stream)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) == 0)
            {
                return null;
            }
            buffer.Add(one[0]);
            if (buffer.Count >= 4 && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n')
            {
                return Encoding.Latin1.GetString(buffer.ToArray(), 0, buffer.Count - 4);
            }
        }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream stream, Dictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Length", out var length))
        {
            var body = new byte[int.Parse(length)];
            await stream.ReadExactlyAsync(body);
            return body;
        }
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            var result = new MemoryStream();
            while (true)
            {
                var sizeLine = await ReadLineAsync(stream);
                var size = Convert.ToInt32(sizeLine.Split(';')[0], 16);
                if (size == 0)
                {
                    await ReadLineAsync(stream);
                    return result.ToArray();
                }
                var chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk);
                result.Write(chunk);
                await ReadLineAsync(stream);
            }
        }
        return [];
    }

    private static async Task<string> ReadLineAsync(Stream stream)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one) > 0)
        {
            if (one[0] == '\n')
            {
                break;
            }
            if (one[0] != '\r')
            {
                line.Add(one[0]);
            }
        }
        return Encoding.ASCII.GetString(line.ToArray());
    }

    private static async Task WriteResponseAsync(Stream stream, ServerResponse response)
    {
        var head = new StringBuilder($"HTTP/1.1 {response.Status} {response.Reason}\r\n");
        foreach (var (name, value) in response.Headers)
        {
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        }
        head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        head.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()));
        await stream.WriteAsync(response.Body);
        await stream.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Dispose();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
        }
        _stop.Dispose();
    }
}

public sealed record ServerRequest(
    string Method,
    string Target,
    string Version,
    Dictionary<string, string> Headers,
    byte[] Body,
    X509Certificate2? ClientCertificate)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

public sealed class ServerResponse
{
    public int Status { get; init; } = 200;

    public string Reason { get; init; } = "OK";

    public List<(string Name, string Value)> Headers { get; init; } = new();

    public byte[] Body { get; init; } = [];

    /// <summary>For Expect: 100-continue requests: answer without reading the body.</summary>
    public bool RejectBeforeBody { get; init; }

    public static ServerResponse Text(string body, int status = 200, params (string Name, string Value)[] headers) => new()
    {
        Status = status,
        Reason = ((HttpStatusCode)status).ToString(),
        Body = Encoding.UTF8.GetBytes(body),
        Headers = [("Content-Type", "text/plain; charset=utf-8"), .. headers]
    };

    public static ServerResponse Redirect(string location, int status = 302) => new()
    {
        Status = status,
        Reason = "Found",
        Headers = [("Location", location)]
    };
}
