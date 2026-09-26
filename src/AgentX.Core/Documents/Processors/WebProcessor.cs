using System.Net;
using System.Net.Sockets;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Web;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Processes URL shortcut files (.url and .webloc) by reading the target URL from the file
/// and then using <see cref="IWebScraperService"/> to extract web content from that URL.
/// <para>
/// This processor enables drag-and-drop of browser URL shortcut files into the Knowledge Vault.
/// Windows .url files use INI-like format with an <c>[InternetShortcut]</c> section containing a
/// <c>URL=</c> entry. macOS .webloc files use XML plist format with a <c>URL</c> string key.
/// </para>
/// <para>
/// The URL comes from a file, possibly one that appeared in a watched folder without the user
/// looking at it, so only public internet hosts are fetched. Loopback, private, link-local
/// and similar targets (this computer, the local network, cloud metadata endpoints) are
/// refused, both as literal addresses and by what the host name resolves to.
/// </para>
/// </summary>
public class WebProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WebProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".url", ".webloc"
    };

    /// <summary>Host names that always mean this computer or the local network.</summary>
    private static readonly string[] LocalHostSuffixes =
    {
        ".localhost", ".local", ".internal", ".lan", ".home.arpa"
    };

    private readonly IWebScraperService _webScraper;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveHost;

    /// <summary>
    /// Initializes a new instance of <see cref="WebProcessor"/>.
    /// </summary>
    /// <param name="webScraper">The web scraper service used to extract content from URLs.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="webScraper"/> is null.</exception>
    public WebProcessor(IWebScraperService webScraper)
        : this(webScraper, null)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="resolveHost"/> replaces DNS resolution.
    /// </summary>
    internal WebProcessor(
        IWebScraperService webScraper,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveHost)
    {
        _webScraper = webScraper ?? throw new ArgumentNullException(nameof(webScraper));
        _resolveHost = resolveHost ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
    }

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    /// <inheritdoc />
    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        Log.Debug("Processing URL shortcut file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("URL shortcut file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = "web",
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
        };

        try
        {
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            // Step 1: Read the URL from the shortcut file
            var url = await ExtractUrlFromShortcutFileAsync(filePath, ext, ct);

            if (string.IsNullOrWhiteSpace(url))
            {
                Log.Warning("No URL found in shortcut file: {FilePath}", filePath);
                throw new DocumentExtractionException("No URL found in shortcut file.");
            }

            if (!_webScraper.IsValidUrl(url))
            {
                Log.Warning("Invalid URL in shortcut file: {Url} from {FilePath}", url, filePath);
                throw new DocumentExtractionException($"Invalid URL: {url}");
            }

            // Step 1b: Only public internet hosts may be fetched from a file-borne URL.
            var blockedReason = await GetBlockedTargetReasonAsync(url, ct);
            if (blockedReason is not null)
            {
                Log.Warning("Refused to fetch {Url} from {FilePath}: {Reason}", url, filePath, blockedReason);
                throw new DocumentExtractionException(blockedReason);
            }

            // Step 2: Extract content from the URL
            var webContent = await _webScraper.ExtractContentAsync(url, ct);

            document.ContentHash = await hashTask;

            if (!webContent.Success)
            {
                Log.Warning(
                    "Failed to extract content from URL {Url}: {Error}",
                    url, webContent.ErrorMessage);
                throw new DocumentExtractionException(webContent.ErrorMessage ?? "Extraction failed.");
            }

            // Step 3: Populate the ProcessedDocument with extracted web content
            document.ExtractedText = webContent.Content;
            document.ExtractedTitle = webContent.Title;
            document.WordCount = webContent.WordCount;
            document.Language = webContent.Language;

            // Store web-specific metadata
            document.Metadata.Custom["sourceUrl"] = url;

            if (!string.IsNullOrEmpty(webContent.Author))
            {
                document.Metadata.Author = webContent.Author;
                document.Metadata.Custom["author"] = webContent.Author;
            }

            if (!string.IsNullOrEmpty(webContent.SiteName))
            {
                document.Metadata.Custom["siteName"] = webContent.SiteName;
            }

            if (!string.IsNullOrEmpty(webContent.Description))
            {
                document.Metadata.Custom["description"] = webContent.Description;
            }

            if (!string.IsNullOrEmpty(webContent.FeaturedImageUrl))
            {
                document.Metadata.Custom["featuredImageUrl"] = webContent.FeaturedImageUrl;
            }

            if (webContent.PublishDate.HasValue)
            {
                document.Metadata.CreatedDate = webContent.PublishDate.Value;
                document.Metadata.Custom["publishDate"] = webContent.PublishDate.Value.ToString("O");
            }

            // File timestamps
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;

            Log.Information(
                "Successfully processed URL shortcut: {FileName} -> {Url} ({WordCount} words)",
                document.FileName, url, document.WordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentExtractionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process URL shortcut file: {FilePath}", filePath);
            throw new DocumentExtractionException(ex.Message, ex);
        }

        return document;
    }

    /// <summary>
    /// Returns why the URL's host must not be fetched, or null when it is a public host.
    /// Host names are resolved and every address must be public; a name that cannot be
    /// resolved is refused too, since what it points at cannot be checked.
    /// </summary>
    private async Task<string?> GetBlockedTargetReasonAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"Invalid URL: {url}";
        }

        var host = uri.IdnHost.TrimEnd('.');
        var blocked = $"Refused to fetch {url}: shortcut files may only point to public internet addresses, " +
                      "not to this computer or the local network.";

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || LocalHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return blocked;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await _resolveHost(host, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not resolve {Host}", host);
                return $"Could not resolve the host in {url}.";
            }
        }

        if (addresses.Length == 0)
        {
            return $"Could not resolve the host in {url}.";
        }

        return addresses.Any(IsNonPublicAddress) ? blocked : null;
    }

    /// <summary>
    /// True for addresses that reach this computer or a private network: loopback,
    /// unspecified, private (RFC 1918), carrier-grade NAT, link-local (including the cloud
    /// metadata endpoint), multicast and reserved IPv4 ranges, and the IPv6 equivalents.
    /// </summary>
    internal static bool IsNonPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] >= 64 && b[1] <= 127,
                169 => b[1] == 254,
                172 => b[1] >= 16 && b[1] <= 31,
                192 => b[1] == 168 || (b[1] == 0 && b[2] == 0),
                198 => b[1] == 18 || b[1] == 19,
                >= 224 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            // Unique local addresses, fc00::/7
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return true;
    }

    /// <summary>
    /// Extracts the target URL from a shortcut file based on its format.
    /// <list type="bullet">
    ///   <item><b>.url (Windows)</b>: INI format with <c>[InternetShortcut]</c> section and <c>URL=</c> key.</item>
    ///   <item><b>.webloc (macOS)</b>: XML plist format with a <c>URL</c> string key.</item>
    /// </list>
    /// </summary>
    /// <param name="filePath">The path to the shortcut file.</param>
    /// <param name="extension">The normalized file extension (lowercase with leading dot).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted URL, or null if no URL could be found.</returns>
    private static async Task<string?> ExtractUrlFromShortcutFileAsync(
        string filePath,
        string extension,
        CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(filePath, ct);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        return extension switch
        {
            ".url" => ExtractUrlFromWindowsShortcut(content),
            ".webloc" => ExtractUrlFromWeblocFile(content),
            _ => null,
        };
    }

    /// <summary>
    /// Parses a Windows .url Internet Shortcut file (INI format) and extracts the URL.
    /// Format:
    /// <code>
    /// [InternetShortcut]
    /// URL=https://example.com/article
    /// </code>
    /// </summary>
    private static string? ExtractUrlFromWindowsShortcut(string content)
    {
        using var reader = new StringReader(content);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                var url = trimmed.Substring(4).Trim();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a macOS .webloc file (XML plist format) and extracts the URL.
    /// Format:
    /// <code>
    /// &lt;?xml version="1.0" encoding="UTF-8"?&gt;
    /// &lt;plist version="1.0"&gt;
    ///   &lt;dict&gt;
    ///     &lt;key&gt;URL&lt;/key&gt;
    ///     &lt;string&gt;https://example.com/article&lt;/string&gt;
    ///   &lt;/dict&gt;
    /// &lt;/plist&gt;
    /// </code>
    /// </summary>
    private static string? ExtractUrlFromWeblocFile(string content)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(content);

            // Navigate: plist > dict > find <key>URL</key> followed by <string>value</string>
            var dict = doc.Descendants("dict").FirstOrDefault();
            if (dict is null)
                return null;

            var elements = dict.Elements().ToList();
            for (var i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name.LocalName == "key"
                    && elements[i].Value.Equals("URL", StringComparison.OrdinalIgnoreCase)
                    && elements[i + 1].Name.LocalName == "string")
                {
                    var url = elements[i + 1].Value.Trim();
                    return string.IsNullOrWhiteSpace(url) ? null : url;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse .webloc file as XML plist");
        }

        return null;
    }
}
