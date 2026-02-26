using System.Text;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Extracts text content from PDF files using PDFsharp 6.1.1.
/// <para>
/// PDFsharp is primarily a PDF creation/manipulation library with limited text extraction
/// capabilities. This processor uses the content stream parser to walk PDF content operators
/// and extract text-show operator strings (Tj, TJ, ', "). For production use with complex
/// PDFs (scanned documents, advanced encodings, CIDFonts), consider substituting a more
/// robust extraction library such as PdfPig or iText7.
/// </para>
/// </summary>
public class PdfProcessor : IDocumentProcessor
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PdfProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

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
        Log.Debug("Processing PDF file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("PDF file not found.", filePath);

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = "pdf",
            FileSizeBytes = fileInfo.Length,
            PageCount = 0,
        };

        try
        {
            // Compute file hash in parallel with PDF processing
            var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

            // PDFsharp requires synchronous file access; offload to thread pool
            var (text, pageCount, metadata) = await Task.Run(() => ExtractPdfContent(filePath), ct);

            document.ContentHash = await hashTask;
            document.ExtractedText = text;
            document.PageCount = pageCount;
            document.WordCount = CountWords(text);
            document.Metadata = metadata;
            document.ExtractedTitle = metadata.Custom.GetValueOrDefault("Title");

            Log.Information(
                "Successfully processed PDF: {FileName} ({PageCount} pages, {WordCount} words)",
                document.FileName, document.PageCount, document.WordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process PDF file: {FilePath}", filePath);
            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"] = ex.Message;
        }

        return document;
    }

    /// <summary>
    /// Opens the PDF and extracts text content from all pages using the content stream parser.
    /// </summary>
    private static (string Text, int PageCount, DocumentMetadata Metadata) ExtractPdfContent(string filePath)
    {
        using var pdfDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

        var pageCount = pdfDocument.PageCount;
        var fullText = new StringBuilder();
        var metadata = new DocumentMetadata();

        // Extract document-level metadata from the Info dictionary
        ExtractMetadata(pdfDocument, metadata);

        for (var i = 0; i < pageCount; i++)
        {
            var page = pdfDocument.Pages[i];
            var pageText = ExtractTextFromPage(page, i + 1);

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                if (fullText.Length > 0)
                    fullText.AppendLine();

                fullText.Append(pageText);
            }
        }

        return (fullText.ToString(), pageCount, metadata);
    }

    /// <summary>
    /// Extracts text from a single PDF page by parsing its content stream operators.
    /// Handles Tj (show string), TJ (show array of strings), ' (move to next line and show string),
    /// and " (set spacing, move to next line, and show string) operators.
    /// </summary>
    private static string ExtractTextFromPage(PdfPage page, int pageNumber)
    {
        try
        {
            var content = ContentReader.ReadContent(page);
            var sb = new StringBuilder();

            ExtractTextFromContentObjects(content, sb);

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to extract text from PDF page {PageNumber}, attempting fallback extraction",
                pageNumber);

            return TryFallbackExtraction(page, pageNumber);
        }
    }

    /// <summary>
    /// Recursively walks the content object tree extracting text from CString operands
    /// of text-show operators.
    /// </summary>
    private static void ExtractTextFromContentObjects(CSequence sequence, StringBuilder sb)
    {
        foreach (var obj in sequence)
        {
            switch (obj)
            {
                case COperator op:
                    HandleOperator(op, sb);
                    break;

                case CSequence nestedSequence:
                    ExtractTextFromContentObjects(nestedSequence, sb);
                    break;
            }
        }
    }

    /// <summary>
    /// Processes a PDF content operator. Text-show operators (Tj, TJ, ', ")
    /// have their string operands extracted.
    /// </summary>
    private static void HandleOperator(COperator op, StringBuilder sb)
    {
        var opName = op.OpCode?.OpCodeName;

        switch (opName)
        {
            // Tj: Show a text string
            case OpCodeName.Tj:
                AppendOperands(op.Operands, sb);
                break;

            // TJ: Show one or more text strings, allowing individual glyph positioning
            case OpCodeName.TJ:
                AppendTjArrayOperands(op.Operands, sb);
                break;

            // ' (single quote): Move to next line and show text string
            case OpCodeName.QuoteSingle:
                sb.AppendLine();
                AppendOperands(op.Operands, sb);
                break;

            // " (double quote): Set word and character spacing, move to next line, show text
            case OpCodeName.QuoteDouble:
                sb.AppendLine();
                AppendOperands(op.Operands, sb);
                break;

            // Td/TD/T*: Text positioning — insert a space to separate words
            case OpCodeName.Td:
            case OpCodeName.TD:
            case OpCodeName.Tx:
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n')
                    sb.Append(' ');
                break;
        }
    }

    /// <summary>
    /// Appends string operands from a simple operator (Tj, ', ").
    /// </summary>
    private static void AppendOperands(CSequence operands, StringBuilder sb)
    {
        foreach (var operand in operands)
        {
            if (operand is CString cString)
            {
                sb.Append(cString.Value);
            }
        }
    }

    /// <summary>
    /// Handles the TJ operator's array operands, which interleave strings with numeric
    /// positioning adjustments. Large negative adjustments typically indicate word boundaries.
    /// </summary>
    private static void AppendTjArrayOperands(CSequence operands, StringBuilder sb)
    {
        foreach (var operand in operands)
        {
            switch (operand)
            {
                case CString cString:
                    sb.Append(cString.Value);
                    break;

                case CArray array:
                    foreach (var element in array)
                    {
                        if (element is CString arrayString)
                        {
                            sb.Append(arrayString.Value);
                        }
                        else if (element is CInteger intVal && intVal.Value < -100)
                        {
                            // Large negative adjustment typically represents a space
                            sb.Append(' ');
                        }
                        else if (element is CReal realVal && realVal.Value < -100)
                        {
                            sb.Append(' ');
                        }
                    }
                    break;

                case CSequence nestedSeq:
                    foreach (var nested in nestedSeq)
                    {
                        if (nested is CString ns)
                            sb.Append(ns.Value);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Fallback text extraction that attempts to read raw stream bytes and extract
    /// literal string objects between parentheses for Tj operators.
    /// This is a last-resort approach when the content parser fails.
    /// </summary>
    private static string TryFallbackExtraction(PdfPage page, int pageNumber)
    {
        try
        {
            // Attempt to get the raw content stream data
            var contents = page.Contents;
            if (contents is null || contents.Elements.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            for (var i = 0; i < contents.Elements.Count; i++)
            {
                if (contents.Elements.GetObject(i) is PdfDictionary streamDict)
                {
                    // Try to access stream bytes if available
                    var stream = streamDict.Stream;
                    if (stream?.Value is not null)
                    {
                        var streamText = ExtractStringsFromStreamBytes(stream.Value);
                        if (!string.IsNullOrWhiteSpace(streamText))
                            sb.Append(streamText);
                    }
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Fallback text extraction also failed for PDF page {PageNumber}",
                pageNumber);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts text strings from raw PDF stream bytes by looking for literal string
    /// objects (text between parentheses) that appear before Tj/TJ operators.
    /// </summary>
    private static string ExtractStringsFromStreamBytes(byte[] bytes)
    {
        var sb = new StringBuilder();
        var text = Encoding.ASCII.GetString(bytes);
        var inString = false;
        var parenDepth = 0;
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    // Handle escape sequences
                    var next = text[i + 1];
                    switch (next)
                    {
                        case 'n': current.Append('\n'); break;
                        case 'r': current.Append('\r'); break;
                        case 't': current.Append('\t'); break;
                        case '(': current.Append('('); break;
                        case ')': current.Append(')'); break;
                        case '\\': current.Append('\\'); break;
                        default: current.Append(next); break;
                    }
                    i++;
                }
                else if (c == '(')
                {
                    parenDepth++;
                    current.Append(c);
                }
                else if (c == ')')
                {
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                        current.Append(c);
                    }
                    else
                    {
                        // End of string — emit if it contains printable text
                        var extracted = current.ToString();
                        if (extracted.Any(ch => char.IsLetterOrDigit(ch)))
                        {
                            if (sb.Length > 0)
                                sb.Append(' ');
                            sb.Append(extracted);
                        }
                        current.Clear();
                        inString = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '(')
            {
                inString = true;
                parenDepth = 0;
                current.Clear();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts document metadata from the PDF Info dictionary.
    /// </summary>
    private static void ExtractMetadata(PdfDocument pdfDocument, DocumentMetadata metadata)
    {
        try
        {
            var info = pdfDocument.Info;
            if (info is null) return;

            if (!string.IsNullOrWhiteSpace(info.Author))
            {
                metadata.Author = info.Author;
                metadata.Custom["Author"] = info.Author;
            }

            if (!string.IsNullOrWhiteSpace(info.Title))
            {
                metadata.Custom["Title"] = info.Title;
            }

            if (!string.IsNullOrWhiteSpace(info.Subject))
            {
                metadata.Subject = info.Subject;
                metadata.Custom["Subject"] = info.Subject;
            }

            if (!string.IsNullOrWhiteSpace(info.Creator))
            {
                metadata.Custom["Creator"] = info.Creator;
            }

            if (!string.IsNullOrWhiteSpace(info.Producer))
            {
                metadata.Custom["Producer"] = info.Producer;
            }

            if (!string.IsNullOrWhiteSpace(info.Keywords))
            {
                metadata.Custom["Keywords"] = info.Keywords;
            }

            if (info.CreationDate != DateTime.MinValue)
            {
                metadata.CreatedDate = info.CreationDate;
            }

            if (info.ModificationDate != DateTime.MinValue)
            {
                metadata.ModifiedDate = info.ModificationDate;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract PDF metadata");
        }
    }

    /// <summary>
    /// Counts words by splitting on whitespace, filtering out empty entries.
    /// </summary>
    private static long CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).LongLength;
    }
}
