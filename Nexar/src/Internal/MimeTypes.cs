namespace Nexar;

/// <summary>
/// Content types for common file extensions.
/// </summary>
internal static class MimeTypes
{
    public const string Default = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".tsv"] = "text/tab-separated-values",
        [".md"] = "text/markdown",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".gz"] = "application/gzip",
        [".tar"] = "application/x-tar",
        [".7z"] = "application/x-7z-compressed",
        [".wasm"] = "application/wasm",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".bmp"] = "image/bmp",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
    };

    public static string FromPath(string path) =>
        ByExtension.TryGetValue(System.IO.Path.GetExtension(path), out var type) ? type : Default;
}
