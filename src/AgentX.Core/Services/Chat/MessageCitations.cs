using System.Text.Json;
using AgentX.Core.Search.Models;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// The stored form of the sources an assistant message cites (<c>MessageEntity.CitationsJson</c>):
/// a JSON array whose entries are web pages (Research Mode) or documents (RAG). The chat reads the
/// web sources back for the bubble, and every exporter describes the entries through here, so
/// they all agree on one format.
/// </summary>
/// <remarks>
/// A web entry is <c>{"kind":"web","title","url","snippet"}</c>. A document entry carries
/// <c>fileName</c>, <c>pageNumber</c> and <c>excerpt</c>; entries without a kind are documents,
/// the format the exporters always read.
/// </remarks>
public static class MessageCitations
{
    private const string WebKind = "web";
    private const int ExcerptLength = 80;

    /// <summary>The web sources as stored JSON, or null when there are none.</summary>
    public static string? Serialize(IReadOnlyList<WebCitation>? webCitations)
    {
        if (webCitations is not { Count: > 0 })
        {
            return null;
        }

        var entries = webCitations.Select(citation => new
        {
            kind = WebKind,
            title = citation.Title,
            url = citation.Url,
            snippet = citation.Snippet,
        });
        return JsonSerializer.Serialize(entries);
    }

    /// <summary>
    /// The web sources among the stored entries, in order. Malformed JSON yields none: citation
    /// metadata must never stop a conversation from opening.
    /// </summary>
    public static IReadOnlyList<WebCitation> ParseWebCitations(string? citationsJson) =>
        Read(citationsJson)
            .Where(entry => entry.IsWeb)
            .Select(entry => new WebCitation
            {
                Title = entry.Title ?? string.Empty,
                Url = entry.Url ?? string.Empty,
                Snippet = entry.Snippet ?? string.Empty,
                Source = WebCitationSource.Web,
            })
            .ToList();

    /// <summary>
    /// One line per stored entry, for exports: "title - url" for a web page, and
    /// "file, page N - "excerpt"" for a document. Malformed JSON yields no lines.
    /// </summary>
    public static IReadOnlyList<string> Describe(string? citationsJson) =>
        Read(citationsJson).Select(Describe).ToList();

    private static string Describe(Entry entry)
    {
        if (entry.IsWeb)
        {
            var url = entry.Url ?? string.Empty;
            return string.IsNullOrWhiteSpace(entry.Title) ? url : $"{entry.Title} - {url}";
        }

        var fileName = entry.FileName ?? "Unknown";
        var description = entry.PageNumber.HasValue
            ? $"{fileName}, page {entry.PageNumber.Value}"
            : fileName;

        if (!string.IsNullOrWhiteSpace(entry.Excerpt))
        {
            var shortExcerpt = entry.Excerpt.Length > ExcerptLength
                ? entry.Excerpt[..ExcerptLength] + "..."
                : entry.Excerpt;
            description += $" - \"{shortExcerpt}\"";
        }

        return description;
    }

    private static List<Entry> Read(string? citationsJson)
    {
        var entries = new List<Entry>();
        if (string.IsNullOrWhiteSpace(citationsJson))
        {
            return entries;
        }

        try
        {
            using var document = JsonDocument.Parse(citationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return entries;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries.Add(new Entry(
                    IsWeb: string.Equals(ReadString(element, "kind"), WebKind, StringComparison.OrdinalIgnoreCase),
                    Title: ReadString(element, "title"),
                    Url: ReadString(element, "url"),
                    Snippet: ReadString(element, "snippet"),
                    FileName: ReadString(element, "fileName"),
                    PageNumber: element.TryGetProperty("pageNumber", out var page) &&
                                page.ValueKind == JsonValueKind.Number &&
                                page.TryGetInt32(out var number)
                        ? number
                        : null,
                    Excerpt: ReadString(element, "excerpt")));
            }
        }
        catch (JsonException)
        {
            // Malformed metadata yields no entries.
        }

        return entries;
    }

    /// <summary>A string property, or null when it is missing or not a string.</summary>
    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record Entry(
        bool IsWeb,
        string? Title,
        string? Url,
        string? Snippet,
        string? FileName,
        int? PageNumber,
        string? Excerpt);
}
