using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentX.Core.Services.Web;

/// <summary>
/// HTTP plumbing shared by the page fetcher, the feed reader and the sitemap parser: redirects
/// are followed one hop at a time (so the final URL is known and every hop can be vetted),
/// response bodies are read under a hard size cap, and text is decoded with the charset the
/// response or the document itself declares.
/// </summary>
internal static class WebHttp
{
    /// <summary>Maximum number of redirects followed for a single request.</summary>
    public const int MaxRedirects = 5;

    /// <summary>
    /// How much of a document is scanned for a declared charset (HTML meta tag or XML
    /// declaration) when neither a byte order mark nor the Content-Type header names one.
    /// </summary>
    private const int CharsetPrescanBytes = 4096;

    private static readonly Regex MetaCharset = new(
        @"<meta\b[^>]*?charset\s*=\s*[""']?\s*(?<name>[A-Za-z0-9._:-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex XmlDeclarationEncoding = new(
        @"^\s*<\?xml\b[^>]*?encoding\s*=\s*[""'](?<name>[A-Za-z0-9._:-]+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static WebHttp()
    {
        // .NET ships only the Unicode encodings, ASCII and Latin-1 by default. Registering the
        // code-pages provider makes windows-1252/1251/1256, Shift_JIS, GB2312 and the other
        // legacy charsets that web pages still declare available to Encoding.GetEncoding.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Sends a GET request and follows up to <see cref="MaxRedirects"/> redirects itself, so the
    /// caller learns the final URL and <paramref name="checkRedirect"/> can refuse a hop before
    /// it is requested. The returned response is the first non-redirect one; the caller
    /// disposes it. Clients passed here should not follow redirects on their own.
    /// </summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="url">The absolute URL to request.</param>
    /// <param name="configureRequest">Optional per-request setup, such as an Accept header.</param>
    /// <param name="checkRedirect">
    /// Optional check called with the current and the next URL before a redirect is followed;
    /// it throws to refuse the redirect.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="HttpRequestException">
    /// Thrown for too many redirects or a redirect to a non-HTTP address.
    /// </exception>
    public static async Task<(HttpResponseMessage Response, Uri FinalUri)> GetFollowingRedirectsAsync(
        HttpClient client,
        Uri url,
        Action<HttpRequestMessage>? configureRequest,
        Func<Uri, Uri, CancellationToken, Task>? checkRedirect,
        CancellationToken ct)
    {
        var current = url;

        for (var redirects = 0; ; redirects++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, current);
            configureRequest?.Invoke(request);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            var location = response.Headers.Location;
            if (!IsRedirect(response.StatusCode) || location is null)
            {
                // A client whose handler follows redirects itself reports the final URL here.
                return (response, response.RequestMessage?.RequestUri ?? current);
            }

            response.Dispose();
            request.Dispose();

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);

            if (redirects >= MaxRedirects)
            {
                throw new HttpRequestException(
                    $"Too many redirects: '{url}' redirected more than {MaxRedirects} times.");
            }

            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException(
                    $"Refused a redirect from '{current}' to '{next}': only HTTP and HTTPS are supported.");
            }

            if (checkRedirect is not null)
            {
                await checkRedirect(current, next, ct).ConfigureAwait(false);
            }

            current = next;
        }
    }

    /// <summary>
    /// Reads an HTTP content stream fully into memory while enforcing a hard byte cap. Aborts as
    /// soon as the cap is exceeded, whether or not the server sent an accurate Content-Length,
    /// and counts decompressed bytes, so a compressed body cannot expand past the cap either.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the body exceeds <paramref name="maxBytes"/>.</exception>
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Pre-size the buffer only when the declared length is present and within
        // the cap; never trust a declared length beyond the cap.
        var declared = content.Headers.ContentLength;
        var initialCapacity = declared.HasValue && declared.Value > 0 && declared.Value <= maxBytes
            ? (int)declared.Value
            : 0;

        using var buffer = new MemoryStream(initialCapacity);
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException(
                    "Content exceeded the maximum allowed size of " +
                    $"{maxBytes / 1024 / 1024} MB while streaming the response.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Decodes a text body. The charset is taken from, in order: a byte order mark, the charset
    /// of the Content-Type header, a charset declared in the document itself (an HTML meta tag
    /// or the XML declaration near the start), and finally UTF-8. The byte order mark is not
    /// part of the returned text.
    /// </summary>
    public static string DecodeText(byte[] bytes, string? headerCharset)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var encoding = TryGetEncoding(headerCharset, fromDocument: false)
                       ?? TryGetEncoding(SniffDeclaredCharset(bytes), fromDocument: true)
                       ?? Encoding.UTF8;

        return encoding.GetString(bytes);
    }

    /// <summary>
    /// Finds a charset declared in the first few kilobytes of a document: an HTML
    /// <c>&lt;meta charset&gt;</c> or <c>http-equiv</c> Content-Type tag, or the encoding of an
    /// XML declaration. The bytes are read as Latin-1, which maps every byte to one character,
    /// so the ASCII markup is found whatever the real encoding is.
    /// </summary>
    internal static string? SniffDeclaredCharset(byte[] bytes)
    {
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, CharsetPrescanBytes));

        var xml = XmlDeclarationEncoding.Match(head);
        if (xml.Success)
        {
            return xml.Groups["name"].Value;
        }

        var meta = MetaCharset.Match(head);
        return meta.Success ? meta.Groups["name"].Value : null;
    }

    private static Encoding? TryGetEncoding(string? name, bool fromDocument)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var label = name.Trim().Trim('"', '\'').ToLowerInvariant();

        // Browsers decode pages labelled Latin-1 or ASCII as windows-1252, its superset that
        // also has curly quotes and dashes; such pages routinely use those characters.
        if (label is "iso-8859-1" or "iso8859-1" or "latin1" or "l1" or "us-ascii" or "ascii")
        {
            label = "windows-1252";
        }

        try
        {
            var encoding = Encoding.GetEncoding(label);

            // A UTF-16/32 label inside the document itself cannot be right (the ASCII markup
            // that carried it would not have been readable), so it falls back to UTF-8.
            if (fromDocument && encoding is UnicodeEncoding or UTF32Encoding)
            {
                return null;
            }

            return encoding;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
