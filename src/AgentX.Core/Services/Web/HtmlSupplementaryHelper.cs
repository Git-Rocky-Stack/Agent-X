using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Static helpers for supplementary HTML extraction that falls outside the scope
/// of the primary <see cref="IHtmlParser"/> and <see cref="IStructuredDataExtractor"/> services.
/// Covers canonical URL resolution, language detection, and table-to-markdown conversion.
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
    /// Converts HTML <c>&lt;table&gt;</c> elements to markdown table format.
    /// Each table is rendered as a pipe-delimited markdown table with a separator
    /// row after the header. Tables are separated by blank lines.
    /// </summary>
    public static string ExtractTablesAsMarkdown(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return string.Empty;

        foreach (var table in tables)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows == null) continue;

            var isFirstRow = true;
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//th | .//td");
                if (cells == null) continue;

                var cellTexts = cells.Select(c =>
                {
                    var text = System.Net.WebUtility.HtmlDecode(c.InnerText ?? string.Empty).Trim();
                    return text.Replace("|", "\\|");
                }).ToList();

                if (cellTexts.Count == 0) continue;

                sb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");

                if (isFirstRow)
                {
                    sb.AppendLine("| " + string.Join(" | ", cellTexts.Select(_ => "---")) + " |");
                    isFirstRow = false;
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
