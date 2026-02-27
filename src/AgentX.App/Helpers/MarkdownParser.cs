using System.Text;
using System.Text.RegularExpressions;

namespace AgentX.App.Helpers;

/// <summary>
/// Types of markdown content segments parsed from AI responses.
/// </summary>
public enum SegmentType
{
    Text,
    CodeBlock,
    InlineCode,
    Bold,
    Heading,
    ListItem
}

/// <summary>
/// Represents a parsed segment of markdown content.
/// </summary>
public class MarkdownSegment
{
    public SegmentType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Language { get; set; }
}

/// <summary>
/// Lightweight markdown parser that splits AI response text into renderable segments.
/// Handles: fenced code blocks (```), inline code (`), bold (**), headings (#), and list items (- / * / 1.).
///
/// Design decisions:
/// - First pass extracts fenced code blocks to prevent inner content from being parsed.
/// - Second pass processes remaining text line-by-line for headings and list items.
/// - Inline formatting (bold, inline code) is handled at render time by the MarkdownMessageControl.
/// </summary>
public static class MarkdownParser
{
    // Regex for fenced code blocks: ```language\n...\n```
    private static readonly Regex CodeBlockRegex = new(
        @"```(\w*)\s*\n([\s\S]*?)```",
        RegexOptions.Compiled);

    // Regex for numbered list items: "1. ", "2. ", etc.
    private static readonly Regex NumberedListRegex = new(
        @"^\d+\.\s",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses the given markdown content string into a list of typed segments
    /// suitable for rendering in the chat UI.
    /// </summary>
    /// <param name="content">The raw markdown text from an AI response.</param>
    /// <returns>An ordered list of <see cref="MarkdownSegment"/> instances.</returns>
    public static List<MarkdownSegment> Parse(string? content)
    {
        var segments = new List<MarkdownSegment>();
        if (string.IsNullOrEmpty(content)) return segments;

        // First pass: extract fenced code blocks, treating everything between ``` pairs
        // as opaque code content that should not be further parsed.
        var lastIndex = 0;
        foreach (Match match in CodeBlockRegex.Matches(content))
        {
            // Add text before the code block
            if (match.Index > lastIndex)
            {
                var textBefore = content[lastIndex..match.Index];
                segments.AddRange(ParseInlineSegments(textBefore));
            }

            // Add the code block segment
            segments.Add(new MarkdownSegment
            {
                Type = SegmentType.CodeBlock,
                Content = match.Groups[2].Value.TrimEnd(),
                Language = string.IsNullOrEmpty(match.Groups[1].Value)
                    ? null
                    : match.Groups[1].Value
            });

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after the last code block (or all text if no code blocks)
        if (lastIndex < content.Length)
        {
            var remaining = content[lastIndex..];
            segments.AddRange(ParseInlineSegments(remaining));
        }

        return segments;
    }

    /// <summary>
    /// Parses non-code-block text into segments for headings, list items, and plain text.
    /// Lines are examined individually for structural markdown elements.
    /// </summary>
    private static List<MarkdownSegment> ParseInlineSegments(string text)
    {
        var segments = new List<MarkdownSegment>();
        if (string.IsNullOrWhiteSpace(text))
        {
            // Preserve whitespace-only text (e.g., newlines between code blocks)
            if (!string.IsNullOrEmpty(text))
                segments.Add(new MarkdownSegment { Type = SegmentType.Text, Content = text });
            return segments;
        }

        // Split by lines to detect headings and list items
        var lines = text.Split('\n');
        var currentBlock = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var trimmedLine = rawLine.TrimStart();

            // Check for heading (# / ## / ###)
            if (trimmedLine.StartsWith("### ") || trimmedLine.StartsWith("## ") || trimmedLine.StartsWith("# "))
            {
                // Flush any accumulated plain text before this heading
                FlushTextBlock(currentBlock, segments);

                var headingContent = trimmedLine.TrimStart('#').Trim();
                segments.Add(new MarkdownSegment { Type = SegmentType.Heading, Content = headingContent });
                continue;
            }

            // Check for unordered list item (- item / * item)
            if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
            {
                FlushTextBlock(currentBlock, segments);

                var itemContent = trimmedLine[2..].Trim();
                segments.Add(new MarkdownSegment { Type = SegmentType.ListItem, Content = itemContent });
                continue;
            }

            // Check for numbered/ordered list item (1. item, 2. item, etc.)
            if (NumberedListRegex.IsMatch(trimmedLine))
            {
                FlushTextBlock(currentBlock, segments);

                var itemContent = NumberedListRegex.Replace(trimmedLine, "").Trim();
                segments.Add(new MarkdownSegment { Type = SegmentType.ListItem, Content = itemContent });
                continue;
            }

            // Regular text line: accumulate into the current block
            currentBlock.AppendLine(rawLine);
        }

        // Flush any remaining accumulated text
        FlushTextBlock(currentBlock, segments);
        return segments;
    }

    /// <summary>
    /// Flushes the accumulated text in the StringBuilder as a single Text segment,
    /// trimming trailing newlines. Clears the StringBuilder afterward.
    /// </summary>
    private static void FlushTextBlock(StringBuilder sb, List<MarkdownSegment> segments)
    {
        if (sb.Length == 0) return;

        var text = sb.ToString().TrimEnd('\r', '\n');
        if (!string.IsNullOrEmpty(text))
        {
            segments.Add(new MarkdownSegment { Type = SegmentType.Text, Content = text });
        }

        sb.Clear();
    }
}
