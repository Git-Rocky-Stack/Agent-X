using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Static helpers for extracting YouTube video transcripts.
/// Handles URL pattern matching, captions URL extraction from page HTML,
/// and XML transcript parsing with a regex fallback for malformed data.
/// <para>
/// Extracted from <see cref="WebScraperService"/> to keep the orchestrator thin
/// while preserving the specialized YouTube parsing logic.
/// </para>
/// </summary>
internal static class YouTubeTranscriptHelper
{
    /// <summary>
    /// Regex to match YouTube video URLs and extract the video ID.
    /// Supports youtube.com/watch?v=, youtu.be/, youtube.com/embed/, and youtube.com/shorts/.
    /// </summary>
    public static readonly Regex YouTubeUrlRegex = new(
        @"(?:https?://)?(?:www\.)?(?:youtube\.com/(?:watch\?.*?v=|embed/|shorts/)|youtu\.be/)(?<id>[\w-]{11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Determines whether the given URL points to a YouTube video.
    /// </summary>
    public static bool IsYouTubeUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && YouTubeUrlRegex.IsMatch(url);
    }

    /// <summary>
    /// Extracts the YouTube video ID from various URL formats.
    /// </summary>
    public static string? ExtractVideoId(string url)
    {
        var match = YouTubeUrlRegex.Match(url);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// Extracts the captions URL from the YouTube watch page HTML.
    /// YouTube embeds caption track info in a JSON structure within the page.
    /// </summary>
    public static string? ExtractCaptionsUrl(string pageHtml, string videoId)
    {
        const string captionTracksMarker = "\"captionTracks\":";
        var markerIndex = pageHtml.IndexOf(captionTracksMarker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            // Try alternative marker
            const string altMarker = "\"captions\":";
            markerIndex = pageHtml.IndexOf(altMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return null;

            var nestedIndex = pageHtml.IndexOf(captionTracksMarker, markerIndex, StringComparison.Ordinal);
            if (nestedIndex < 0)
                return null;

            markerIndex = nestedIndex;
        }

        // Find the array start
        var arrayStart = pageHtml.IndexOf('[', markerIndex);
        if (arrayStart < 0)
            return null;

        // Find the matching array end
        var depth = 0;
        var arrayEnd = -1;
        for (var i = arrayStart; i < pageHtml.Length; i++)
        {
            switch (pageHtml[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        arrayEnd = i;
                        goto FoundEnd;
                    }
                    break;
            }
        }

    FoundEnd:
        if (arrayEnd < 0)
            return null;

        var captionTracksJson = pageHtml.Substring(arrayStart, arrayEnd - arrayStart + 1);

        // Prefer English captions, then fall back to any available
        return ExtractCaptionUrlFromJson(captionTracksJson, preferredLanguage: "en")
               ?? ExtractCaptionUrlFromJson(captionTracksJson, preferredLanguage: null);
    }

    /// <summary>
    /// Parses YouTube's XML transcript format into clean plain text with timestamps.
    /// Falls back to regex parsing for malformed XML.
    /// </summary>
    public static string ParseTranscriptXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var sb = new StringBuilder();

            foreach (var element in doc.Descendants("text"))
            {
                var startAttr = element.Attribute("start")?.Value;
                var text = element.Value;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                text = System.Net.WebUtility.HtmlDecode(text).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (!string.IsNullOrEmpty(startAttr) && double.TryParse(startAttr,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var startSeconds))
                {
                    var timeSpan = TimeSpan.FromSeconds(startSeconds);
                    var timestamp = timeSpan.TotalHours >= 1
                        ? timeSpan.ToString(@"h\:mm\:ss")
                        : timeSpan.ToString(@"m\:ss");

                    sb.AppendLine($"[{timestamp}] {text}");
                }
                else
                {
                    sb.AppendLine(text);
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception)
        {
            return ParseTranscriptFallback(xml);
        }
    }

    /// <summary>
    /// Extracts a caption track URL from the JSON array string.
    /// Optionally filters by language code.
    /// </summary>
    private static string? ExtractCaptionUrlFromJson(string jsonArray, string? preferredLanguage)
    {
        var searchStart = 0;

        while (searchStart < jsonArray.Length)
        {
            const string baseUrlKey = "\"baseUrl\":\"";
            var urlKeyIndex = jsonArray.IndexOf(baseUrlKey, searchStart, StringComparison.Ordinal);
            if (urlKeyIndex < 0)
                break;

            var urlStart = urlKeyIndex + baseUrlKey.Length;
            var urlEnd = jsonArray.IndexOf('"', urlStart);
            if (urlEnd < 0)
                break;

            var url = jsonArray.Substring(urlStart, urlEnd - urlStart);
            url = url.Replace("\\u0026", "&").Replace("\\/", "/");

            if (preferredLanguage is null)
                return url;

            // Check if this track matches the preferred language
            var contextStart = Math.Max(0, urlKeyIndex - 200);
            var contextEnd = Math.Min(jsonArray.Length, urlKeyIndex + url.Length + 200);
            var context = jsonArray.Substring(contextStart, contextEnd - contextStart);

            var langPattern = $"\"languageCode\":\"{preferredLanguage}\"";
            if (context.Contains(langPattern, StringComparison.OrdinalIgnoreCase))
                return url;

            var vssPattern1 = $"\"vssId\":\".{preferredLanguage}\"";
            var vssPattern2 = $"\"vssId\":\"a.{preferredLanguage}\"";
            if (context.Contains(vssPattern1, StringComparison.OrdinalIgnoreCase)
                || context.Contains(vssPattern2, StringComparison.OrdinalIgnoreCase))
                return url;

            searchStart = urlEnd + 1;
        }

        return null;
    }

    /// <summary>
    /// Fallback transcript parser using regex for malformed XML transcript data.
    /// </summary>
    private static string ParseTranscriptFallback(string xml)
    {
        var textPattern = new Regex(
            @"<text[^>]*>(?<content>[^<]*)</text>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var sb = new StringBuilder();

        foreach (Match match in textPattern.Matches(xml))
        {
            var content = System.Net.WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
            if (!string.IsNullOrWhiteSpace(content))
                sb.AppendLine(content);
        }

        return sb.ToString().Trim();
    }
}
