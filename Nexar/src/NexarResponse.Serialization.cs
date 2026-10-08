using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Nexar;

public sealed partial class NexarResponse
{
    /// <summary>The client's registered serializers, used by <see cref="As{T}(CancellationToken)"/>.</summary>
    internal IReadOnlyList<IContentSerializer> Serializers { get; set; } = [];

    /// <summary>
    /// Reads the body with <paramref name="serializer"/>, e.g. <c>res.As&lt;Invoice&gt;(XmlContentSerializer.Default)</c>.
    /// </summary>
    /// <exception cref="NexarException">The body is empty, null, or cannot be read as <typeparamref name="T"/> (<see cref="ErrorKind.Decode"/>).</exception>
    public async Task<T> As<T>(IContentSerializer serializer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        await BufferAsync(cancellationToken).ConfigureAwait(false);
        if (_response.Content.Headers.ContentLength == 0)
        {
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: the response body is empty.", Url);
        }

        T? value;
        try
        {
            value = await serializer.DeserializeAsync<T>(_response.Content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not NexarException and not OperationCanceledException)
        {
            // XmlSerializer reports bad input as InvalidOperationException; other formats use their own types.
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name} as {serializer.MediaType}: {ex.GetBaseException().Message}", Url, innerException: ex);
        }

        if (value is null && Nullable.GetUnderlyingType(typeof(T)) == null)
        {
            throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: the response body is null.", Url);
        }
        return value!;
    }

    /// <summary>
    /// Reads the body with the registered serializer that matches its <c>Content-Type</c>
    /// (<see cref="ClientBuilder.Serializer"/>). Without a match, JSON bodies use the client's JSON options and XML
    /// bodies use <see cref="XmlContentSerializer"/>.
    /// </summary>
    /// <exception cref="NexarException">No serializer reads the content type, or the body cannot be decoded (<see cref="ErrorKind.Decode"/>).</exception>
    [RequiresUnreferencedCode(AotMessages.Json)]
    [RequiresDynamicCode(AotMessages.Json)]
    public Task<T> As<T>(CancellationToken cancellationToken = default)
    {
        var mediaType = ContentType?.MediaType ?? "application/octet-stream";
        var serializer = Serializers.FirstOrDefault(s => s.CanRead(mediaType));
        if (serializer != null)
        {
            return As<T>(serializer, cancellationToken);
        }
        if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return Json<T>(cancellationToken);
        }
        if (XmlContentSerializer.Default.CanRead(mediaType))
        {
            return As<T>(XmlContentSerializer.Default, cancellationToken);
        }
        throw new NexarException(ErrorKind.Decode, $"Cannot decode {typeof(T).Name}: no serializer reads '{mediaType}'.", Url);
    }
}
