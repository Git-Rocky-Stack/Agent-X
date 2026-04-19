using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Models;

namespace AgentX.Core.Services.Export.Formats;

/// <summary>
/// Exports conversations and search results to structured JSON format.
/// Includes full metadata, messages, and citations for archival purposes.
/// </summary>
public class JsonExport : IExportFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ExportFormat Format => ExportFormat.Json;
    public string FileExtension => ".json";

    public bool Supports<T>() =>
        typeof(T) == typeof(ConversationEntity) ||
        typeof(T) == typeof(IEnumerable<ConversationEntity>) ||
        typeof(T) == typeof(SearchResultExportItem) ||
        typeof(T) == typeof(IEnumerable<SearchResultExportItem>);

    public async Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            return data switch
            {
                ConversationEntity conversation => RenderConversation(conversation, options),
                IEnumerable<ConversationEntity> conversations => RenderConversations(conversations, options),
                SearchResultExportItem result => RenderSearchResult(result, options),
                IEnumerable<SearchResultExportItem> results => RenderSearchResults(results, options),
                _ => throw new NotSupportedException($"JSON export does not support type {typeof(T).Name}")
            };
        }, ct);
    }

    private static string RenderConversation(ConversationEntity conversation, ExportOptions options)
    {
        var export = new
        {
            exportMetadata = new
            {
                title = options.Title ?? conversation.Title,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                conversationCount = 1,
            },
            conversations = new[]
            {
                new
                {
                    id = conversation.Id,
                    title = conversation.Title,
                    systemPrompt = conversation.SystemPrompt,
                    modelId = conversation.ModelId,
                    createdAt = conversation.CreatedAt,
                    updatedAt = conversation.UpdatedAt,
                    isPinned = conversation.IsPinned,
                    isArchived = conversation.IsArchived,
                    messageCount = conversation.MessageCount,
                    tokensUsed = conversation.TokensUsed,
                    messages = conversation.Messages
                        .OrderBy(m => m.SortOrder)
                        .Select(m => new
                        {
                            id = m.Id,
                            role = m.Role,
                            content = m.Content,
                            timestamp = m.Timestamp,
                            tokenCount = options.IncludeMetadata ? m.TokenCount : (int?)null,
                            generationTimeMs = options.IncludeMetadata ? m.GenerationTimeMs : null,
                            modelId = options.IncludeModelInfo ? m.ModelId : null,
                            citations = options.IncludeCitations ? m.CitationsJson : null,
                            sortOrder = m.SortOrder,
                        }).ToArray(),
                }
            },
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    private static string RenderConversations(IEnumerable<ConversationEntity> conversations, ExportOptions options)
    {
        var list = conversations.ToList();
        var title = options.Title ?? $"Agent-X Conversations Export ({list.Count})";

        var export = new
        {
            exportMetadata = new
            {
                title,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                conversationCount = list.Count,
            },
            conversations = list.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                systemPrompt = c.SystemPrompt,
                modelId = c.ModelId,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt,
                isPinned = c.IsPinned,
                isArchived = c.IsArchived,
                messageCount = c.MessageCount,
                tokensUsed = c.TokensUsed,
                messages = c.Messages
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new
                    {
                        id = m.Id,
                        role = m.Role,
                        content = m.Content,
                        timestamp = m.Timestamp,
                        tokenCount = options.IncludeMetadata ? m.TokenCount : (int?)null,
                        generationTimeMs = options.IncludeMetadata ? m.GenerationTimeMs : null,
                        modelId = options.IncludeModelInfo ? m.ModelId : null,
                        citations = options.IncludeCitations ? m.CitationsJson : null,
                        sortOrder = m.SortOrder,
                    }).ToArray(),
            }).ToArray(),
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    private static string RenderSearchResult(SearchResultExportItem result, ExportOptions options)
    {
        var export = new
        {
            exportMetadata = new
            {
                title = options.Title ?? result.DocumentName,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                resultCount = 1,
            },
            query = result.Query,
            results = new[]
            {
                new
                {
                    documentName = result.DocumentName,
                    relevanceScore = result.RelevanceScore,
                    content = result.Content,
                    citations = result.Citations,
                }
            },
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    private static string RenderSearchResults(IEnumerable<SearchResultExportItem> results, ExportOptions options)
    {
        var list = results.ToList();
        var title = options.Title ?? $"Search Results ({list.Count})";

        var export = new
        {
            exportMetadata = new
            {
                title,
                exportedAt = DateTime.UtcNow,
                exportedBy = "Agent-X",
                format = "json",
                resultCount = list.Count,
            },
            results = list.Select((r, i) => new
            {
                index = i + 1,
                documentName = r.DocumentName,
                relevanceScore = r.RelevanceScore,
                content = r.Content,
                citations = r.Citations,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }
}
