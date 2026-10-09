namespace Nexar;

/// <summary>
/// Writes and reads request and response bodies in a format such as XML, MessagePack or Newtonsoft.Json.
/// Register serializers with <see cref="ClientBuilder.Serializer"/>; Nexar's own <c>Json()</c> methods keep using
/// System.Text.Json. <see cref="XmlContentSerializer"/> ships in the box.
/// </summary>
public interface IContentSerializer
{
    /// <summary>The media type written by <see cref="Serialize{T}"/>, e.g. <c>application/xml</c>.</summary>
    string MediaType { get; }

    /// <summary>
    /// Whether this serializer reads <paramref name="mediaType"/>. By default: the same media type, or a
    /// structured-syntax suffix of it (<c>application/vnd.api+xml</c> for <c>application/xml</c>).
    /// </summary>
    bool CanRead(string mediaType)
    {
        if (string.Equals(mediaType, MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var subtype = MediaType[(MediaType.IndexOf('/') + 1)..];
        return mediaType.EndsWith("+" + subtype, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the request body for <paramref name="value"/>, with its <c>Content-Type</c> set.</summary>
    HttpContent Serialize<T>(T value);

    /// <summary>
    /// Reads <paramref name="content"/> as <typeparamref name="T"/>. The content is already buffered, so it can be
    /// read in any way, and its headers (e.g. the charset) are available.
    /// </summary>
    ValueTask<T?> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken);
}
