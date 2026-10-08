using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Nexar;

/// <summary>
/// Reads and writes XML with <see cref="XmlSerializer"/>: <c>application/xml</c>, <c>text/xml</c> and <c>+xml</c> types.
/// </summary>
/// <example>
/// <code>
/// await client.Post("/orders").Body(order, XmlContentSerializer.Default).Send();
/// var invoice = await client.Get("/invoices/1").Send().As&lt;Invoice&gt;(XmlContentSerializer.Default);
/// </code>
/// </example>
public sealed class XmlContentSerializer : IContentSerializer
{
    private const string TrimmingMessage = "XmlSerializer uses reflection and code generation, which trimming and Native AOT do not support.";

    private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = new();
    private readonly Encoding _encoding;

    /// <summary>Writes UTF-8 XML.</summary>
    [RequiresUnreferencedCode(TrimmingMessage)]
    [RequiresDynamicCode(TrimmingMessage)]
    public XmlContentSerializer() : this(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
    {
    }

    /// <summary>Writes XML in <paramref name="encoding"/>, with a matching charset.</summary>
    [RequiresUnreferencedCode(TrimmingMessage)]
    [RequiresDynamicCode(TrimmingMessage)]
    public XmlContentSerializer(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _encoding = encoding;
    }

    /// <summary>A shared UTF-8 instance.</summary>
    public static XmlContentSerializer Default
    {
        [RequiresUnreferencedCode(TrimmingMessage)]
        [RequiresDynamicCode(TrimmingMessage)]
        get => DefaultInstance.Value;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Default is annotated.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Default is annotated.")]
    private static readonly Lazy<XmlContentSerializer> DefaultInstance = new(() => new XmlContentSerializer());

    /// <inheritdoc />
    public string MediaType => "application/xml";

    /// <inheritdoc />
    public bool CanRead(string mediaType) =>
        mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The constructors are annotated.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The constructors are annotated.")]
    public HttpContent Serialize<T>(T value)
    {
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = _encoding, Indent = false }))
        {
            For(typeof(T)).Serialize(writer, value);
        }
        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaType) { CharSet = _encoding.WebName };
        return content;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The constructors are annotated.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The constructors are annotated.")]
    public async ValueTask<T?> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');

        // A charset in Content-Type wins over the XML declaration; without one, XmlReader detects the encoding.
        using var reader = string.IsNullOrEmpty(charset)
            ? XmlReader.Create(new MemoryStream(bytes))
            : XmlReader.Create(new StringReader(Encoding.GetEncoding(charset).GetString(bytes)));
        return (T?)For(typeof(T)).Deserialize(reader);
    }

    [RequiresUnreferencedCode(TrimmingMessage)]
    [RequiresDynamicCode(TrimmingMessage)]
    private static XmlSerializer For(Type type) => Serializers.GetOrAdd(type, t => new XmlSerializer(t));
}
