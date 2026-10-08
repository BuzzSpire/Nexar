namespace Nexar;

/// <summary>
/// One event from a <c>text/event-stream</c> response, read with <c>Events()</c>.
/// </summary>
/// <param name="Event">The event type; <c>message</c> unless the server sent an <c>event:</c> field.</param>
/// <param name="Data">The data, with multiple <c>data:</c> lines joined by <c>\n</c>.</param>
/// <param name="Id">The last event ID seen on the stream (it carries over to later events), or null.</param>
/// <param name="Retry">The reconnection time the server asked for in this event, or null.</param>
public sealed record ServerSentEvent(string Event, string Data, string? Id, TimeSpan? Retry);
