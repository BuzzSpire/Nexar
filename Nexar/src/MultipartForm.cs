using System.Net.Http.Headers;

namespace Nexar;

/// <summary>
/// Builds a <c>multipart/form-data</c> body for <see cref="RequestBuilder.Multipart"/>.
/// </summary>
/// <example>
/// <code>
/// var form = new MultipartForm()
///     .Text("title", "Holiday")
///     .File("photo", bytes, "beach.jpg", "image/jpeg");
/// </code>
/// </example>
public sealed class MultipartForm
{
    private readonly List<Action<MultipartFormDataContent>> _parts = new();

    internal bool IsReplayable { get; private set; } = true;

    /// <summary>
    /// Adds a text field.
    /// </summary>
    public MultipartForm Text(string name, string value)
    {
        _parts.Add(content => content.Add(new StringContent(value), name));
        return this;
    }

    /// <summary>
    /// Adds a file from a byte array.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="contentType"/> is not a valid media type.</exception>
    public MultipartForm File(string name, byte[] data, string fileName, string? contentType = null)
    {
        Validate(contentType);
        _parts.Add(content => content.Add(WithContentType(new ByteArrayContent(data), contentType), name, fileName));
        return this;
    }

    /// <summary>
    /// Adds a file from a stream. Requests with a stream part are never retried.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="contentType"/> is not a valid media type.</exception>
    public MultipartForm File(string name, Stream data, string fileName, string? contentType = null)
    {
        Validate(contentType);
        IsReplayable = false;
        _parts.Add(content => content.Add(WithContentType(new StreamContent(data), contentType), name, fileName));
        return this;
    }

    /// <summary>
    /// Adds the file at <paramref name="path"/>, named after the file, with a content type from its extension
    /// unless given. The file is reopened for every attempt, so the request can be retried.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException"><paramref name="contentType"/> is not a valid media type.</exception>
    public MultipartForm File(string name, string path, string? contentType = null)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.", path);
        }
        var type = contentType ?? MimeTypes.FromPath(path);
        Validate(type);
        var fileName = System.IO.Path.GetFileName(path);
        _parts.Add(content => content.Add(WithContentType(new StreamContent(System.IO.File.OpenRead(path)), type), name, fileName));
        return this;
    }

    internal HttpContent Build()
    {
        var content = new MultipartFormDataContent();
        foreach (var part in _parts)
        {
            part(content);
        }
        return content;
    }

    private static void Validate(string? contentType)
    {
        if (contentType != null)
        {
            MediaTypeHeaderValue.Parse(contentType);
        }
    }

    private static HttpContent WithContentType(HttpContent content, string? contentType)
    {
        if (contentType != null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        return content;
    }
}
