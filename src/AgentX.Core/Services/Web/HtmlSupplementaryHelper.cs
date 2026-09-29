using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Static helpers for supplementary HTML extraction that falls outside the scope
/// of the primary <see cref="IHtmlParser"/> and <see cref="IStructuredDataExtractor"/> services.
/// Covers canonical URL resolution, language detection, and table-to-Markdown conversion
/// (used by <see cref="HtmlParser"/> for tables inside the extracted article).
/// <para>
/// Extracted from <see cref="WebScraperService"/> to keep the orchestrator thin
/// while preserving the specialized extraction logic.
/// </para>
/// </summary>
internal static class HtmlSupplementaryHelper
{
    /// <summary>
    /// Extracts the canonical URL from <c>&lt;link rel="canonical"&gt;</c>.
    /// </summary>
    public static string? ExtractCanonicalUrl(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var link = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
        return link?.GetAttributeValue("href", null);
    }

    /// <summary>
    /// Extracts the language from the <c>&lt;html lang="..."&gt;</c> attribute
    /// or the <c>&lt;meta http-equiv="content-language"&gt;</c> tag.
    /// </summary>
    public static string? ExtractLanguage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var htmlNode = doc.DocumentNode.SelectSingleNode("//html");
        var lang = htmlNode?.GetAttributeValue("lang", null);
        if (!string.IsNullOrWhiteSpace(lang))
            return lang;

        var metaNode = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='content-language']");
        return metaNode?.GetAttributeValue("content", null)?.Trim();
    }

    /// <summary>
    /// Converts one HTML <c>&lt;table&gt;</c> element to a pipe-delimited Markdown table with a
    /// separator row after the first row. Cell text is whitespace-collapsed so a line break
    /// inside a cell cannot split a Markdown row. Returns an empty string for a table without
    /// cells. The HTML parser calls this for data tables inside the extracted article only, so
    /// navigation and layout tables elsewhere on the page never reach the imported text.
    /// </summary>
    public static string TableToMarkdown(HtmlNode table)
    {
        var sb = new StringBuilder();
        var rows = table.SelectNodes(".//tr");
        if (rows == null) return string.Empty;

        var isFirstRow = true;
        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//th | .//td");
            if (cells == null) continue;

            var cellTexts = cells.Select(c =>
            {
                var text = WebUtility.HtmlDecode(c.InnerText ?? string.Empty);
                return CellWhitespace.Replace(text, " ").Trim().Replace("|", "\\|");
            }).ToList();

            if (cellTexts.Count == 0) continue;

            sb.Append("| ").Append(string.Join(" | ", cellTexts)).Append(" |\n");

            if (isFirstRow)
            {
                sb.Append("| ").Append(string.Join(" | ", cellTexts.Select(_ => "---"))).Append(" |\n");
                isFirstRow = false;
            }
        }

        return sb.ToString();
    }

    private static readonly Regex CellWhitespace = new(@"\s+", RegexOptions.Compiled);
}
