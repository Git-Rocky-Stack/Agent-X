using AgentX.Core.AI;
using AgentX.Core.Documents.Models;
using Serilog;

namespace AgentX.Core.Documents;

/// <summary>
/// Recursive character text splitter that breaks text into overlapping chunks.
///
/// Algorithm hierarchy:
/// 1. Split by paragraphs (double newlines)
/// 2. If a paragraph exceeds chunkSize, split by sentence boundaries
/// 3. If a sentence exceeds chunkSize, split by word boundaries
/// 4. Apply overlap: the last N tokens of the previous chunk are prepended to the next
///
/// Token counting uses ITokenCounter for accurate model-specific tokenization.
/// </summary>
public sealed class ChunkingService : IChunkingService
{
    private readonly ILogger _logger;
    private readonly ITokenCounter? _tokenCounter;

    /// <summary>
    /// Sentence-ending patterns used to split paragraphs that exceed the chunk size.
    /// Ordered by preference: period+space, exclamation+space, question+space, period+newline.
    /// </summary>
    private static readonly string[] SentenceSeparators = { ". ", "! ", "? ", ".\n" };

    /// <summary>
    /// Paragraph separator: two consecutive newlines (with optional whitespace between).
    /// </summary>
    private const string ParagraphSeparator = "\n\n";

    /// <summary>
    /// Form-feed character used by many PDF extractors to denote page breaks.
    /// </summary>
    private const char PageBreakChar = '\f';

    public ChunkingService()
    {
        _logger = Log.ForContext<ChunkingService>();
        _tokenCounter = null;
    }

    public ChunkingService(ILogger logger)
    {
        _logger = logger ?? Log.ForContext<ChunkingService>();
        _tokenCounter = null;
    }

    public ChunkingService(ITokenCounter tokenCounter, ILogger logger)
    {
        _tokenCounter = tokenCounter;
        _logger = logger ?? Log.ForContext<ChunkingService>();
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> ChunkDocument(
        ProcessedDocument document,
        int chunkSize = 512,
        int chunkOverlap = 50)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            _logger.Warning("Document '{FileName}' has no extracted text, returning empty chunk list",
                document.FileName);
            return Array.Empty<DocumentChunk>();
        }

        ValidateParameters(chunkSize, chunkOverlap);

        _logger.Information(
            "Chunking document '{FileName}' ({PageCount} pages, {WordCount} words) with chunkSize={ChunkSize}, overlap={Overlap}",
            document.FileName, document.PageCount, document.WordCount, chunkSize, chunkOverlap);

        // If the document has multiple pages and the text contains form-feed page breaks,
        // process page-by-page to preserve page number metadata.
        if (document.PageCount > 1 && document.ExtractedText.Contains(PageBreakChar))
        {
            return ChunkByPages(document, chunkSize, chunkOverlap);
        }

        // Otherwise, chunk the full extracted text as a single stream.
        var chunks = ChunkText(document.ExtractedText, chunkSize, chunkOverlap);

        _logger.Information("Document '{FileName}' chunked into {Count} chunks", document.FileName, chunks.Count);
        return chunks;
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> ChunkText(
        string text,
        int chunkSize = 512,
        int chunkOverlap = 50,
        string? sectionTitle = null,
        int? pageNumber = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<DocumentChunk>();
        }

        ValidateParameters(chunkSize, chunkOverlap);

        // Step 1: Split text into paragraphs by double newlines.
        var paragraphs = SplitIntoParagraphs(text);

        // Step 2: Break paragraphs into sentence-level segments that fit within chunkSize.
        var segments = new List<TextSegment>();
        foreach (var paragraph in paragraphs)
        {
            var tokenCount = CountTokens(paragraph.Text);

            if (tokenCount <= chunkSize)
            {
                segments.Add(paragraph);
            }
            else
            {
                // Paragraph is too large; split at sentence boundaries.
                var sentenceSegments = SplitBySentences(paragraph);
                foreach (var sentenceSegment in sentenceSegments)
                {
                    var sentenceTokens = CountTokens(sentenceSegment.Text);

                    if (sentenceTokens <= chunkSize)
                    {
                        segments.Add(sentenceSegment);
                    }
                    else
                    {
                        // Sentence is still too large; split at word boundaries.
                        var wordSegments = SplitByWords(sentenceSegment, chunkSize);
                        segments.AddRange(wordSegments);
                    }
                }
            }
        }

        // Step 3: Group segments into chunks up to chunkSize, applying overlap.
        var chunks = GroupSegmentsIntoChunks(segments, text, chunkSize, chunkOverlap, sectionTitle, pageNumber);

        _logger.Debug("ChunkText produced {Count} chunks from {TextLength} chars", chunks.Count, text.Length);
        return chunks;
    }

    // ── Private: Page-level chunking ────────────────────────────────────

    /// <summary>
    /// Splits a multi-page document by form-feed characters, then chunks each page
    /// independently while maintaining a global chunk index.
    /// </summary>
    private IReadOnlyList<DocumentChunk> ChunkByPages(
        ProcessedDocument document,
        int chunkSize,
        int chunkOverlap)
    {
        var allChunks = new List<DocumentChunk>();
        var pages = document.ExtractedText.Split(PageBreakChar);
        var globalCharOffset = 0;
        var globalIndex = 0;

        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var pageText = pages[pageIndex];

            if (string.IsNullOrWhiteSpace(pageText))
            {
                // Account for the form-feed character between pages.
                globalCharOffset += pageText.Length + (pageIndex < pages.Length - 1 ? 1 : 0);
                continue;
            }

            // Page numbers are 1-based.
            var pageNumber = pageIndex + 1;
            var pageChunks = ChunkText(pageText, chunkSize, chunkOverlap, pageNumber: pageNumber);

            foreach (var chunk in pageChunks)
            {
                allChunks.Add(new DocumentChunk
                {
                    Index = globalIndex++,
                    Content = chunk.Content,
                    StartCharOffset = globalCharOffset + chunk.StartCharOffset,
                    EndCharOffset = globalCharOffset + chunk.EndCharOffset,
                    PageNumber = pageNumber,
                    SectionTitle = chunk.SectionTitle,
                    TokenCount = chunk.TokenCount
                });
            }

            // Move the global offset past this page's text + the form-feed separator.
            globalCharOffset += pageText.Length + (pageIndex < pages.Length - 1 ? 1 : 0);
        }

        _logger.Information(
            "Page-based chunking of '{FileName}' produced {Count} chunks across {PageCount} pages",
            document.FileName, allChunks.Count, document.PageCount);

        return allChunks.AsReadOnly();
    }

    // ── Private: Text splitting ─────────────────────────────────────────

    /// <summary>
    /// Splits text into paragraph-level segments delimited by double newlines.
    /// Tracks the character offset of each paragraph within the source text.
    /// </summary>
    private static List<TextSegment> SplitIntoParagraphs(string text)
    {
        var segments = new List<TextSegment>();
        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var separatorIndex = text.IndexOf(ParagraphSeparator, startIndex, StringComparison.Ordinal);

            if (separatorIndex < 0)
            {
                // No more paragraph separators; the rest of the text is the final paragraph.
                var remaining = text[startIndex..];
                if (!string.IsNullOrWhiteSpace(remaining))
                {
                    segments.Add(new TextSegment(remaining, startIndex));
                }
                break;
            }

            // Extract the paragraph before the separator.
            var paragraphText = text[startIndex..separatorIndex];
            if (!string.IsNullOrWhiteSpace(paragraphText))
            {
                segments.Add(new TextSegment(paragraphText, startIndex));
            }

            // Skip past the separator (which is "\n\n").
            startIndex = separatorIndex + ParagraphSeparator.Length;
        }

        return segments;
    }

    /// <summary>
    /// Splits a paragraph that exceeds chunkSize into sentence-level segments.
    /// Uses sentence-ending patterns (". ", "! ", "? ", ".\n") as delimiters.
    /// </summary>
    private static List<TextSegment> SplitBySentences(TextSegment paragraph)
    {
        var segments = new List<TextSegment>();
        var text = paragraph.Text;
        var baseOffset = paragraph.CharOffset;
        var startIndex = 0;

        while (startIndex < text.Length)
        {
            // Find the nearest sentence boundary.
            var nearestEnd = -1;
            var separatorLength = 0;

            foreach (var sep in SentenceSeparators)
            {
                var idx = text.IndexOf(sep, startIndex, StringComparison.Ordinal);
                if (idx >= 0 && (nearestEnd < 0 || idx < nearestEnd))
                {
                    nearestEnd = idx;
                    // Include the sentence-ending punctuation in this segment,
                    // but not the trailing space/newline (which is part of the separator gap).
                    separatorLength = sep.Length;
                }
            }

            if (nearestEnd < 0)
            {
                // No more sentence boundaries; the rest is the final sentence.
                var remaining = text[startIndex..];
                if (!string.IsNullOrWhiteSpace(remaining))
                {
                    segments.Add(new TextSegment(remaining, baseOffset + startIndex));
                }
                break;
            }

            // Include the punctuation mark in the sentence text (e.g., the period in ". ").
            var sentenceEnd = nearestEnd + 1; // +1 to include the punctuation character
            var sentenceText = text[startIndex..sentenceEnd];

            if (!string.IsNullOrWhiteSpace(sentenceText))
            {
                segments.Add(new TextSegment(sentenceText, baseOffset + startIndex));
            }

            startIndex = nearestEnd + separatorLength;
        }

        return segments;
    }

    /// <summary>
    /// Splits a segment that exceeds chunkSize at word boundaries into multiple
    /// smaller segments, each at most chunkSize tokens.
    /// </summary>
    private static List<TextSegment> SplitByWords(TextSegment segment, int chunkSize)
    {
        var segments = new List<TextSegment>();
        var words = segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return segments;

        var currentWords = new List<string>();
        var currentCharStart = 0;
        var charPosition = 0;

        // Walk through the original text to track accurate character positions.
        var text = segment.Text;
        var wordIndex = 0;

        foreach (var word in words)
        {
            // Find the actual position of this word in the text.
            var wordPos = text.IndexOf(word, charPosition, StringComparison.Ordinal);
            if (wordPos < 0)
                wordPos = charPosition; // Fallback; should not happen with well-formed text.

            if (currentWords.Count == 0)
            {
                currentCharStart = wordPos;
            }

            currentWords.Add(word);

            if (currentWords.Count >= chunkSize)
            {
                // Emit the current group of words as a segment.
                var segmentText = string.Join(' ', currentWords);
                segments.Add(new TextSegment(segmentText, segment.CharOffset + currentCharStart));

                currentWords.Clear();
                charPosition = wordPos + word.Length;
            }
            else
            {
                charPosition = wordPos + word.Length;
            }

            wordIndex++;
        }

        // Emit any remaining words.
        if (currentWords.Count > 0)
        {
            var segmentText = string.Join(' ', currentWords);
            segments.Add(new TextSegment(segmentText, segment.CharOffset + currentCharStart));
        }

        return segments;
    }

    // ── Private: Chunk grouping with overlap ────────────────────────────

    /// <summary>
    /// Groups small segments into chunks up to chunkSize tokens, applying overlap
    /// by prepending the last chunkOverlap tokens from the previous chunk.
    /// </summary>
    private List<DocumentChunk> GroupSegmentsIntoChunks(
        List<TextSegment> segments,
        string sourceText,
        int chunkSize,
        int chunkOverlap,
        string? sectionTitle,
        int? pageNumber)
    {
        if (segments.Count == 0)
            return new List<DocumentChunk>();

        var chunks = new List<DocumentChunk>();
        var currentSegments = new List<TextSegment>();
        var currentTokenCount = 0;

        foreach (var segment in segments)
        {
            var segmentTokens = CountTokens(segment.Text);

            // If adding this segment would exceed chunkSize and we already have content,
            // finalize the current chunk and start a new one.
            if (currentTokenCount + segmentTokens > chunkSize && currentSegments.Count > 0)
            {
                var chunk = BuildChunk(currentSegments, chunks.Count, sectionTitle, pageNumber);
                chunks.Add(chunk);

                // Apply overlap: carry over the tail tokens from the current chunk.
                currentSegments = GetOverlapSegments(currentSegments, chunkOverlap);
                currentTokenCount = currentSegments.Sum(s => CountTokens(s.Text));
            }

            currentSegments.Add(segment);
            currentTokenCount += segmentTokens;
        }

        // Finalize the last chunk.
        if (currentSegments.Count > 0)
        {
            var lastChunk = BuildChunk(currentSegments, chunks.Count, sectionTitle, pageNumber);
            chunks.Add(lastChunk);
        }

        return chunks;
    }

    /// <summary>
    /// Builds a DocumentChunk from a list of text segments by concatenating their content.
    /// </summary>
    private DocumentChunk BuildChunk(
        List<TextSegment> segments,
        int index,
        string? sectionTitle,
        int? pageNumber)
    {
        // Concatenate segments with a single space separator where segments aren't
        // already separated by whitespace.
        var content = string.Join(" ", segments.Select(s => s.Text.Trim()));
        var startOffset = segments[0].CharOffset;
        var lastSegment = segments[^1];
        var endOffset = lastSegment.CharOffset + lastSegment.Text.Length;

        return new DocumentChunk
        {
            Index = index,
            Content = content,
            StartCharOffset = startOffset,
            EndCharOffset = endOffset,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            TokenCount = CountTokens(content)
        };
    }

    /// <summary>
    /// Extracts the trailing segments from the current chunk that together comprise
    /// approximately chunkOverlap tokens, for use as the overlap prefix of the next chunk.
    /// </summary>
    private List<TextSegment> GetOverlapSegments(List<TextSegment> segments, int chunkOverlap)
    {
        if (chunkOverlap <= 0 || segments.Count == 0)
            return new List<TextSegment>();

        var overlapSegments = new List<TextSegment>();
        var overlapTokens = 0;

        // Walk backward through segments to collect up to chunkOverlap tokens.
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var segmentTokens = CountTokens(segments[i].Text);
            overlapTokens += segmentTokens;
            overlapSegments.Insert(0, segments[i]);

            if (overlapTokens >= chunkOverlap)
                break;
        }

        return overlapSegments;
    }

    // ── Private: Token counting ─────────────────────────────────────────

    /// <summary>
    /// Counts tokens in text using the token counter service if available,
    /// otherwise falls back to word count approximation.
    /// This is a conservative approximation: real tokenizers typically produce ~1.3 tokens
    /// per word, so word count provides a safe lower bound for chunking purposes.
    /// </summary>
    private int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Use accurate token counting if available
        if (_tokenCounter is not null)
            return _tokenCounter.CountTokens(text);

        // Fallback to word count approximation
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // ── Private: Validation ─────────────────────────────────────────────

    private static void ValidateParameters(int chunkSize, int chunkOverlap)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize,
                "Chunk size must be a positive integer.");

        if (chunkOverlap < 0)
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap), chunkOverlap,
                "Chunk overlap must be a non-negative integer.");

        if (chunkOverlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap), chunkOverlap,
                "Chunk overlap must be less than chunk size to ensure forward progress.");
    }

    // ── Private: Internal types ─────────────────────────────────────────

    /// <summary>
    /// Represents a segment of text with its character offset within the original source text.
    /// Used internally during the splitting process to preserve positional information.
    /// </summary>
    private readonly record struct TextSegment(string Text, int CharOffset);
}
