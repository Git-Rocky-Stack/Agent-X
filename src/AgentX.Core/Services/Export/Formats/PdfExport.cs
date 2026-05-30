using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Export.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Serilog;

namespace AgentX.Core.Services.Export.Formats;

/// <summary>
/// PDF export format implementation using QuestPDF.
/// Generates professional, print-ready PDF documents with proper formatting.
/// </summary>
public sealed class PdfExport : IExportFormat
{
    private readonly ILogger _logger;

    public ExportFormat Format => ExportFormat.Pdf;
    public string FileExtension => ".pdf";

    public PdfExport(ILogger logger)
    {
        _logger = logger.ForContext<PdfExport>();
    }

    public bool Supports<T>() => typeof(T) == typeof(ConversationEntity)
                              || typeof(T) == typeof(IReadOnlyList<ConversationEntity>)
                              || typeof(T) == typeof(IReadOnlyList<SearchResultExportItem>);

    public async Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var result = data switch
        {
            ConversationEntity conversation => await RenderConversationAsync(conversation, options, ct),
            IReadOnlyList<ConversationEntity> conversations => await RenderConversationsAsync(conversations, options, ct),
            IReadOnlyList<SearchResultExportItem> results => await RenderSearchResultsAsync(results, options, ct),
            _ => throw new NotSupportedException($"PdfExport does not support type {typeof(T).Name}")
        };

        return result;
    }

    /// <summary>
    /// Renders a PDF file to the specified output path.
    /// </summary>
    public Task<string> RenderToFileAsync<T>(T data, ExportOptions options, string outputPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var document = data switch
        {
            ConversationEntity conversation => CreateConversationDocument(conversation, options),
            IReadOnlyList<ConversationEntity> conversations => CreateConversationsDocument(conversations, options),
            IReadOnlyList<SearchResultExportItem> results => CreateSearchResultsDocument(results, options),
            _ => throw new NotSupportedException($"PdfExport does not support type {typeof(T).Name}")
        };

        try
        {
            document.GeneratePdf(outputPath);
            _logger.Debug("PDF generated successfully at '{Path}'", outputPath);
            return Task.FromResult(outputPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "QuestPDF generation failed");
            throw new InvalidOperationException($"PDF generation failed: {ex.Message}", ex);
        }
    }

    private Task<byte[]> RenderConversationAsync(ConversationEntity conversation, ExportOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var document = CreateConversationDocument(conversation, options);
        return Task.FromResult(document.GeneratePdf());
    }

    private Task<byte[]> RenderConversationsAsync(IReadOnlyList<ConversationEntity> conversations, ExportOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var document = CreateConversationsDocument(conversations, options);
        return Task.FromResult(document.GeneratePdf());
    }

    private Task<byte[]> RenderSearchResultsAsync(IReadOnlyList<SearchResultExportItem> results, ExportOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var document = CreateSearchResultsDocument(results, options);
        return Task.FromResult(document.GeneratePdf());
    }

    private IDocument CreateConversationDocument(ConversationEntity conversation, ExportOptions options)
    {
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#212529"));

                // Header
                page.Header().Column(headerCol =>
                {
                    headerCol.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Agent-X").Bold().FontSize(16).FontColor("#0d6efd");
                        row.ConstantItem(160).AlignRight().Text(text =>
                        {
                            text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(8).FontColor("#6c757d");
                        });
                    });
                    headerCol.Item().PaddingTop(4).LineHorizontal(1).LineColor("#dee2e6");
                    headerCol.Item().PaddingBottom(8);
                });

                // Content
                page.Content().Column(contentCol =>
                {
                    contentCol.Item().Text(conversation.Title).Bold().FontSize(14).FontColor("#212529");
                    contentCol.Item().PaddingBottom(12);

                    // Metadata
                    if (options.IncludeMetadata)
                    {
                        contentCol.Item().Background("#f8f9fa").Border(1).BorderColor("#dee2e6").Padding(8).Column(metaCol =>
                        {
                            metaCol.Item().Text($"Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC").FontSize(8).FontColor("#6c757d");
                            metaCol.Item().Text($"Messages: {conversation.MessageCount}  |  Tokens: {conversation.TokensUsed:N0}").FontSize(8).FontColor("#6c757d");
                            if (!string.IsNullOrWhiteSpace(conversation.ModelId))
                                metaCol.Item().Text($"Model: {conversation.ModelId}").FontSize(8).FontColor("#6c757d");
                        });
                        contentCol.Item().PaddingBottom(10);
                    }

                    // System prompt
                    if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
                    {
                        contentCol.Item().Text("System Prompt").Bold().FontSize(10).FontColor("#6c757d");
                        contentCol.Item().PaddingLeft(8).BorderLeft(3).BorderColor("#0d6efd").PaddingLeft(8).PaddingVertical(4)
                            .Text(conversation.SystemPrompt).FontSize(9).Italic().FontColor("#6c757d");
                        contentCol.Item().PaddingBottom(10);
                    }

                    // Messages
                    var messages = conversation.Messages.OrderBy(m => m.SortOrder).ToList();
                    var citationsList = new List<string>();

                    foreach (var message in messages)
                    {
                        if (message.Role == "system") continue;

                        var isUser = message.Role == "user";
                        var bgColor = isUser ? "#e3f2fd" : "#f5f5f5";
                        var accentColor = isUser ? "#0d6efd" : "#adb5bd";
                        var roleLabel = GetRoleLabel(message.Role);

                        contentCol.Item().PaddingBottom(6).BorderLeft(3).BorderColor(accentColor).Background(bgColor).Padding(8).Column(msgCol =>
                        {
                            msgCol.Item().Row(row =>
                            {
                                row.RelativeItem().Text(roleLabel).Bold().FontSize(8).FontColor(accentColor);
                                if (options.IncludeTimestamps)
                                    row.ConstantItem(120).AlignRight().Text(message.Timestamp.ToString("yyyy-MM-dd HH:mm")).FontSize(7).FontColor("#adb5bd");
                            });

                            msgCol.Item().PaddingTop(4).Text(message.Content).FontSize(9.5f);

                            if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
                                msgCol.Item().PaddingTop(4).Text($"Model: {message.ModelId}").FontSize(7).Italic().FontColor("#adb5bd");

                            if (options.IncludeMetadata && message.Role == "assistant")
                            {
                                var metaParts = new List<string>();
                                if (message.TokenCount > 0) metaParts.Add($"Tokens: {message.TokenCount:N0}");
                                if (message.GenerationTimeMs.HasValue) metaParts.Add($"{message.GenerationTimeMs.Value:F0}ms");
                                if (metaParts.Count > 0)
                                    msgCol.Item().PaddingTop(2).Text(string.Join("  |  ", metaParts)).FontSize(7).FontColor("#adb5bd");
                            }
                        });

                        if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
                        {
                            var citations = TryParseCitations(message.CitationsJson);
                            citationsList.AddRange(citations);
                        }
                    }

                    // Citations
                    if (citationsList.Count > 0)
                    {
                        contentCol.Item().PaddingTop(12);
                        contentCol.Item().Text("Citations").Bold().FontSize(10).FontColor("#6c757d");
                        contentCol.Item().PaddingTop(4).Column(citCol =>
                        {
                            for (var i = 0; i < citationsList.Count; i++)
                                citCol.Item().Text($"{i + 1}. {citationsList[i]}").FontSize(8).FontColor("#6c757d");
                        });
                    }
                });

                // Footer
                page.Footer().Column(footerCol =>
                {
                    footerCol.Item().LineHorizontal(1).LineColor("#dee2e6");
                    footerCol.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Exported from Agent-X").FontSize(7).FontColor("#adb5bd");
                        row.ConstantItem(80).AlignRight().Text(text =>
                        {
                            text.Span("Page ").FontSize(7).FontColor("#adb5bd");
                            text.CurrentPageNumber().FontSize(7).FontColor("#adb5bd");
                            text.Span(" / ").FontSize(7).FontColor("#adb5bd");
                            text.TotalPages().FontSize(7).FontColor("#adb5bd");
                        });
                    });
                });
            });
        });
    }

    private IDocument CreateConversationsDocument(IReadOnlyList<ConversationEntity> conversations, ExportOptions options)
    {
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#212529"));

                // Header
                page.Header().Column(headerCol =>
                {
                    headerCol.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Agent-X").Bold().FontSize(16).FontColor("#0d6efd");
                        row.ConstantItem(160).AlignRight().Text(text =>
                        {
                            text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(8).FontColor("#6c757d");
                        });
                    });
                    headerCol.Item().PaddingTop(4).LineHorizontal(1).LineColor("#dee2e6");
                    headerCol.Item().PaddingBottom(8);
                });

                // Content
                page.Content().Column(contentCol =>
                {
                    var title = $"Agent-X Conversations Export ({conversations.Count})";
                    contentCol.Item().Text(title).Bold().FontSize(14).FontColor("#212529");
                    contentCol.Item().PaddingBottom(12);

                    for (var ci = 0; ci < conversations.Count; ci++)
                    {
                        var conversation = conversations[ci];

                        if (ci > 0)
                        {
                            contentCol.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#adb5bd");
                            contentCol.Item().PaddingBottom(6).Text(conversation.Title).Bold().FontSize(12).FontColor("#212529");
                        }

                        // Metadata
                        if (options.IncludeMetadata)
                        {
                            contentCol.Item().Background("#f8f9fa").Border(1).BorderColor("#dee2e6").Padding(8).Column(metaCol =>
                            {
                                metaCol.Item().Text($"Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC").FontSize(8).FontColor("#6c757d");
                                metaCol.Item().Text($"Messages: {conversation.MessageCount}  |  Tokens: {conversation.TokensUsed:N0}").FontSize(8).FontColor("#6c757d");
                                if (!string.IsNullOrWhiteSpace(conversation.ModelId))
                                    metaCol.Item().Text($"Model: {conversation.ModelId}").FontSize(8).FontColor("#6c757d");
                            });
                            contentCol.Item().PaddingBottom(10);
                        }

                        // System prompt
                        if (!string.IsNullOrWhiteSpace(conversation.SystemPrompt))
                        {
                            contentCol.Item().Text("System Prompt").Bold().FontSize(10).FontColor("#6c757d");
                            contentCol.Item().PaddingLeft(8).BorderLeft(3).BorderColor("#0d6efd").PaddingLeft(8).PaddingVertical(4)
                                .Text(conversation.SystemPrompt).FontSize(9).Italic().FontColor("#6c757d");
                            contentCol.Item().PaddingBottom(10);
                        }

                        // Messages
                        var messages = conversation.Messages.OrderBy(m => m.SortOrder).ToList();
                        var citationsList = new List<string>();

                        foreach (var message in messages)
                        {
                            if (message.Role == "system") continue;

                            var isUser = message.Role == "user";
                            var bgColor = isUser ? "#e3f2fd" : "#f5f5f5";
                            var accentColor = isUser ? "#0d6efd" : "#adb5bd";
                            var roleLabel = GetRoleLabel(message.Role);

                            contentCol.Item().PaddingBottom(6).BorderLeft(3).BorderColor(accentColor).Background(bgColor).Padding(8).Column(msgCol =>
                            {
                                msgCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(roleLabel).Bold().FontSize(8).FontColor(accentColor);
                                    if (options.IncludeTimestamps)
                                        row.ConstantItem(120).AlignRight().Text(message.Timestamp.ToString("yyyy-MM-dd HH:mm")).FontSize(7).FontColor("#adb5bd");
                                });

                                msgCol.Item().PaddingTop(4).Text(message.Content).FontSize(9.5f);

                                if (options.IncludeModelInfo && !string.IsNullOrWhiteSpace(message.ModelId))
                                    msgCol.Item().PaddingTop(4).Text($"Model: {message.ModelId}").FontSize(7).Italic().FontColor("#adb5bd");

                                if (options.IncludeMetadata && message.Role == "assistant")
                                {
                                    var metaParts = new List<string>();
                                    if (message.TokenCount > 0) metaParts.Add($"Tokens: {message.TokenCount:N0}");
                                    if (message.GenerationTimeMs.HasValue) metaParts.Add($"{message.GenerationTimeMs.Value:F0}ms");
                                    if (metaParts.Count > 0)
                                        msgCol.Item().PaddingTop(2).Text(string.Join("  |  ", metaParts)).FontSize(7).FontColor("#adb5bd");
                                }
                            });

                            if (options.IncludeCitations && !string.IsNullOrWhiteSpace(message.CitationsJson))
                            {
                                var citations = TryParseCitations(message.CitationsJson);
                                citationsList.AddRange(citations);
                            }
                        }

                        // Citations
                        if (citationsList.Count > 0)
                        {
                            contentCol.Item().PaddingTop(12);
                            contentCol.Item().Text("Citations").Bold().FontSize(10).FontColor("#6c757d");
                            contentCol.Item().PaddingTop(4).Column(citCol =>
                            {
                                for (var i = 0; i < citationsList.Count; i++)
                                    citCol.Item().Text($"{i + 1}. {citationsList[i]}").FontSize(8).FontColor("#6c757d");
                            });
                        }
                    }
                });

                // Footer
                page.Footer().Column(footerCol =>
                {
                    footerCol.Item().LineHorizontal(1).LineColor("#dee2e6");
                    footerCol.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Exported from Agent-X").FontSize(7).FontColor("#adb5bd");
                        row.ConstantItem(80).AlignRight().Text(text =>
                        {
                            text.Span("Page ").FontSize(7).FontColor("#adb5bd");
                            text.CurrentPageNumber().FontSize(7).FontColor("#adb5bd");
                            text.Span(" / ").FontSize(7).FontColor("#adb5bd");
                            text.TotalPages().FontSize(7).FontColor("#adb5bd");
                        });
                    });
                });
            });
        });
    }

    private IDocument CreateSearchResultsDocument(IReadOnlyList<SearchResultExportItem> results, ExportOptions options)
    {
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#212529"));

                // Header
                page.Header().Column(headerCol =>
                {
                    headerCol.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Agent-X").Bold().FontSize(16).FontColor("#0d6efd");
                        row.ConstantItem(160).AlignRight().Text(text =>
                        {
                            text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(8).FontColor("#6c757d");
                        });
                    });
                    headerCol.Item().PaddingTop(4).LineHorizontal(1).LineColor("#dee2e6");
                    headerCol.Item().PaddingBottom(8);
                });

                // Content
                page.Content().Column(contentCol =>
                {
                    var title = options.Title ?? $"Search Results: {results.FirstOrDefault()?.Query ?? "Search"}";
                    contentCol.Item().Text(title).Bold().FontSize(14).FontColor("#212529");
                    contentCol.Item().PaddingTop(4).Text($"Query: {results.FirstOrDefault()?.Query ?? "N/A"}").FontSize(10).FontColor("#6c757d");
                    contentCol.Item().Text($"Results: {results.Count}").FontSize(9).FontColor("#adb5bd");
                    contentCol.Item().PaddingBottom(12);

                    for (var i = 0; i < results.Count; i++)
                    {
                        var result = results[i];

                        contentCol.Item().PaddingBottom(8).Background("#f8f9fa").Border(1).BorderColor("#dee2e6").Padding(10).Column(resultCol =>
                        {
                            resultCol.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Result {i + 1}: {result.DocumentName}").Bold().FontSize(10).FontColor("#212529");
                                if (options.IncludeMetadata)
                                    row.ConstantItem(80).AlignRight().Text($"{result.RelevanceScore:P1}").FontSize(9).FontColor("#0d6efd");
                            });

                            resultCol.Item().PaddingTop(6).Text(result.Content).FontSize(9.5f);

                            if (options.IncludeCitations && result.Citations.Count > 0)
                            {
                                resultCol.Item().PaddingTop(6).Column(citCol =>
                                {
                                    citCol.Item().Text("Sources:").Bold().FontSize(8).FontColor("#6c757d");
                                    foreach (var citation in result.Citations)
                                        citCol.Item().PaddingLeft(8).Text($"- {citation}").FontSize(8).FontColor("#6c757d");
                                });
                            }
                        });
                    }
                });

                // Footer
                page.Footer().Column(footerCol =>
                {
                    footerCol.Item().LineHorizontal(1).LineColor("#dee2e6");
                    footerCol.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("Exported from Agent-X").FontSize(7).FontColor("#adb5bd");
                        row.ConstantItem(80).AlignRight().Text(text =>
                        {
                            text.Span("Page ").FontSize(7).FontColor("#adb5bd");
                            text.CurrentPageNumber().FontSize(7).FontColor("#adb5bd");
                            text.Span(" / ").FontSize(7).FontColor("#adb5bd");
                            text.TotalPages().FontSize(7).FontColor("#adb5bd");
                        });
                    });
                });
            });
        });
    }

    private static string GetRoleLabel(string role) => role.ToLowerInvariant() switch
    {
        "user" => "User",
        "assistant" => "Assistant",
        "system" => "System",
        _ => role
    };

    private static List<string> TryParseCitations(string citationsJson)
    {
        var result = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(citationsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return result;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var fileName = element.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "Unknown" : "Unknown";
                var pageNumber = element.TryGetProperty("pageNumber", out var pn) && pn.ValueKind == System.Text.Json.JsonValueKind.Number ? pn.GetInt32() : (int?)null;
                var excerpt = element.TryGetProperty("excerpt", out var ex) ? ex.GetString() : null;

                var description = pageNumber.HasValue ? $"{fileName}, page {pageNumber.Value}" : fileName;
                if (!string.IsNullOrWhiteSpace(excerpt))
                {
                    var shortExcerpt = excerpt.Length > 80 ? excerpt[..80] + "..." : excerpt;
                    description += $" - \"{shortExcerpt}\"";
                }
                result.Add(description);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Citation metadata is optional and decorative; malformed or partial
            // JSON must not fail the export. Return whatever parsed successfully.
        }
        return result;
    }
}
