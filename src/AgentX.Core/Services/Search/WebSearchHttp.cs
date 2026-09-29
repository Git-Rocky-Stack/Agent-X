using System.Net;

namespace AgentX.Core.Services.Search;

/// <summary>
/// HTTP setup shared by the web search providers.
/// </summary>
internal static class WebSearchHttp
{
    /// <summary>
    /// Creates a client whose handler negotiates and transparently decodes gzip, deflate and
    /// Brotli. Requests must not add their own Accept-Encoding header: HttpClient only decodes
    /// encodings its handler asked for, so a hand-written "gzip" header returns a compressed
    /// body that then fails to parse as JSON.
    /// </summary>
    public static HttpClient CreateClient() => new(CreateHandler());

    /// <summary>The decompressing handler behind <see cref="CreateClient"/>.</summary>
    public static HttpClientHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
    };
}
