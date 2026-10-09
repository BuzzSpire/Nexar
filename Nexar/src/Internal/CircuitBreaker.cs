using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nexar;

/// <summary>The outcome of one attempt, as the circuit breaker counts it.</summary>
internal enum CircuitOutcome
{
    Success,
    Failure,

    /// <summary>Neither, e.g. cancelled by the caller; only releases a half-open probe.</summary>
    Neutral
}

internal sealed record CircuitBreakerSettings(double FailureRatio, int MinimumThroughput, TimeSpan SamplingDuration, TimeSpan BreakDuration, TimeProvider Clock);

/// <summary>
/// One circuit per host: closed (counting outcomes), open (failing fast), half-open (one probe decides).
/// </summary>
internal sealed class CircuitBreaker(CircuitBreakerSettings settings, ILogger logger)
{
    private readonly ConcurrentDictionary<string, HostCircuit> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lets an attempt through, or throws <see cref="ErrorKind.CircuitOpen"/>.</summary>
    public void Enter(Uri url)
    {
        var circuit = _hosts.GetOrAdd(url.Authority, _ => new HostCircuit());
        var now = settings.Clock.GetUtcNow();
        lock (circuit)
        {
            if (circuit.State == CircuitState.Open && now >= circuit.OpenUntil)
            {
                Transition(circuit, url.Authority, CircuitState.HalfOpen);
            }

            switch (circuit.State)
            {
                case CircuitState.Open:
                    throw Rejected(url, circuit.OpenUntil - now);
                case CircuitState.HalfOpen when circuit.ProbeInFlight:
                    throw Rejected(url, settings.BreakDuration);
                case CircuitState.HalfOpen:
                    circuit.ProbeInFlight = true;
                    break;
            }
        }
    }

    public void Record(Uri url, CircuitOutcome outcome)
    {
        if (!_hosts.TryGetValue(url.Authority, out var circuit))
        {
            return;
        }
        var now = settings.Clock.GetUtcNow();
        lock (circuit)
        {
            switch (circuit.State)
            {
                case CircuitState.HalfOpen:
                    circuit.ProbeInFlight = false;
                    if (outcome == CircuitOutcome.Failure)
                    {
                        Open(circuit, url.Authority, now);
                    }
                    else if (outcome == CircuitOutcome.Success)
                    {
                        circuit.Samples.Clear();
                        Transition(circuit, url.Authority, CircuitState.Closed);
                    }
                    break;

                case CircuitState.Closed when outcome != CircuitOutcome.Neutral:
                    circuit.Samples.Enqueue((now, outcome == CircuitOutcome.Failure));
                    while (circuit.Samples.Count > 0 && circuit.Samples.Peek().At <= now - settings.SamplingDuration)
                    {
                        circuit.Samples.Dequeue();
                    }
                    var failures = circuit.Samples.Count(s => s.Failed);
                    if (circuit.Samples.Count >= settings.MinimumThroughput && failures >= settings.FailureRatio * circuit.Samples.Count)
                    {
                        Open(circuit, url.Authority, now);
                    }
                    break;
            }
        }
    }

    private void Open(HostCircuit circuit, string host, DateTimeOffset now)
    {
        circuit.OpenUntil = now + settings.BreakDuration;
        circuit.Samples.Clear();
        Transition(circuit, host, CircuitState.Open);
    }

    private void Transition(HostCircuit circuit, string host, CircuitState state)
    {
        circuit.State = state;
        Telemetry.CircuitStateChanges.Add(1, new TagList { { "server.address", host }, { "nexar.circuit.state", state.ToString().ToLowerInvariant() } });
        if (state == CircuitState.Open)
        {
            logger.LogWarning("Circuit for {Host} opened for {BreakSeconds:0} s", host, settings.BreakDuration.TotalSeconds);
        }
        else
        {
            logger.LogInformation("Circuit for {Host} is {State}", host, state);
        }
    }

    private static NexarException Rejected(Uri url, TimeSpan retryAfter) =>
        new(ErrorKind.CircuitOpen, $"The circuit for {url.Authority} is open; failing fast instead of sending to {url}.", url)
        {
            RetryAfter = retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.Zero
        };

    private enum CircuitState { Closed, Open, HalfOpen }

    private sealed class HostCircuit
    {
        public CircuitState State = CircuitState.Closed;
        public DateTimeOffset OpenUntil;
        public bool ProbeInFlight;
        public readonly Queue<(DateTimeOffset At, bool Failed)> Samples = new();
    }
}
