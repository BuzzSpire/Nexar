using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Nexar.Test;

/// <summary>
/// Activities and metrics are process-wide, so these tests filter by a unique host per test
/// and do not run in parallel with each other.
/// </summary>
[Collection(nameof(TelemetryTests))]
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public sealed class TelemetryTests : IDisposable
{
    private readonly string _host = $"t{Guid.NewGuid():N}.test";
    private readonly ConcurrentBag<Activity> _activities = new();
    private readonly ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public TelemetryTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Nexar",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("server.address") as string == _host)
                {
                    _activities.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Nexar")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _meterListener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i.Name, v, t));
        _meterListener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i.Name, v, t));
        _meterListener.Start();
    }

    private void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }
        if (dictionary.GetValueOrDefault("server.address") as string == _host)
        {
            _measurements.Enqueue((name, value, dictionary));
        }
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private NexarClient Client(FakeHandler handler, Action<ClientBuilder>? configure = null) =>
        TestClient.Create(handler, b =>
        {
            b.BaseUrl($"https://{_host}");
            configure?.Invoke(b);
        });

    [Fact]
    public async Task SuccessfulRequestCreatesSpanWithSemanticConventionTags()
    {
        using var client = Client(new FakeHandler(HttpStatusCode.Created));

        using var res = await client.Post("/orders").Query("api_key", "secret-key").Query("page", 2).Send();

        var activity = Assert.Single(_activities);
        Assert.Equal("POST", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
        Assert.Equal(443, activity.GetTagItem("server.port"));
        Assert.Equal(201, activity.GetTagItem("http.response.status_code"));
        Assert.Equal($"https://{_host}/orders?api_key=REDACTED&page=2", activity.GetTagItem("url.full"));
        Assert.Null(activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    public async Task ErrorStatusMarksSpanAsError()
    {
        using var client = Client(new FakeHandler(HttpStatusCode.ServiceUnavailable));

        using var res = await client.Get("/").Send();

        var activity = Assert.Single(_activities);
        Assert.Equal("503", activity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task FailureRecordsErrorKind()
    {
        using var client = Client(new FakeHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")));

        await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        var activity = Assert.Single(_activities);
        Assert.Equal("connect", activity.GetTagItem("error.type"));
        Assert.Null(activity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task RetriesAreRecordedAsEventsAndResendCount()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.OK });
        using var client = Client(new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue())), b => b.Retry(3, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        var activity = Assert.Single(_activities);
        Assert.Equal(2, activity.GetTagItem("http.request.resend_count"));
        var events = activity.Events.Where(e => e.Name == "nexar.resend").ToList();
        Assert.Equal(new[] { "503", "502" }, events.Select(e => e.Tags.First(t => t.Key == "nexar.resend.reason").Value));
        Assert.Equal(2, _measurements.Where(m => m.Name == "nexar.client.resends").Sum(m => m.Value));
    }

    [Fact]
    public async Task MetricsRecordDurationAndActiveRequests()
    {
        using var client = Client(new FakeHandler(HttpStatusCode.OK));

        using var res = await client.Get("/").Send();

        var duration = Assert.Single(_measurements, m => m.Name == "http.client.request.duration");
        Assert.True(duration.Value >= 0);
        Assert.Equal("GET", duration.Tags["http.request.method"]);
        Assert.Equal(200, duration.Tags["http.response.status_code"]);
        Assert.False(duration.Tags.ContainsKey("url.full"));   // no high-cardinality data in metrics

        var active = _measurements.Where(m => m.Name == "http.client.active_requests").Select(m => m.Value).ToList();
        Assert.Equal(new[] { 1.0, -1.0 }, active);
    }

    // ---- Logging -----------------------------------------------------------------

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    [Fact]
    public async Task LogsRequestLineAtInformation()
    {
        var logger = new ListLogger();
        using var client = Client(new FakeHandler(HttpStatusCode.OK), b => b.Logger(logger));

        using var res = await client.Get("/users").Query("access_token", "tok").Send();

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains($"HTTP GET https://{_host}/users?access_token=REDACTED responded 200", entry.Message);
    }

    [Fact]
    public async Task SecretsNeverReachLogsOrSpans()
    {
        var logger = new ListLogger();
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Respond(HttpStatusCode.OK);
            response.Headers.Add("Set-Cookie", "session=cookie-secret");
            return response;
        });
        using var client = Client(handler, b => b
            .Logger(logger)
            .Auth(Auth.ApiKeyHeader("X-Custom-Key", "header-secret"))
            .RedactHeaders("X-Internal")
            .RedactQueryParameters("ticket"));

        using var res = await client.Get("/")
            .Header("Cookie", "c=cookie-secret")
            .Header("X-Internal", "internal-secret")
            .Query("ticket", "query-secret")
            .Send();
        using var res2 = await client.Get("/").Auth(Auth.ApiKeyQuery("sk", "custom-query-secret")).Send();

        var everything = string.Join("\n", logger.Entries.Select(e => e.Message))
            + string.Join("\n", _activities.SelectMany(a => a.Tags).Select(t => t.Value));
        Assert.DoesNotContain("secret", everything);
        Assert.Contains("X-Custom-Key: REDACTED", everything);
        Assert.Contains("Set-Cookie: REDACTED", everything);
    }

    [Fact]
    public async Task RetriesAreLoggedAtDebug()
    {
        var logger = new ListLogger();
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK });
        using var client = Client(new FakeHandler(_ => FakeHandler.Respond(statuses.Dequeue())), b => b.Logger(logger).Retry(1, TimeSpan.Zero));

        using var res = await client.Get("/").Send();

        var debug = Assert.Single(logger.Entries, e => e.Level == LogLevel.Debug);
        Assert.Contains("(503)", debug.Message);
    }

    [Fact]
    public async Task FailuresAreLoggedAtWarning()
    {
        var logger = new ListLogger();
        using var client = Client(new FakeHandler((_, _) => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no such host")),
            b => b.Logger(logger));

        await Assert.ThrowsAsync<NexarException>(() => client.Get("/").Send());

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("failed (Connect)", warning.Message);
    }
}
