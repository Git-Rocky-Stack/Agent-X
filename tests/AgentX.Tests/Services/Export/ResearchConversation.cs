using AgentX.Core.Data.Entities;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Chat;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// A conversation with two Research Mode answers saved the way chat saves them: each with the
/// model that wrote it and its own web sources, which its [n] markers number from 1.
/// </summary>
internal static class ResearchConversation
{
    public const string FirstAnswer = "Version 2 ships a new parser [1] and drops the old API [2].";
    public const string SecondAnswer = "A blog post covers it [1].";

    public static ConversationEntity Create() => new()
    {
        Id = 7,
        Title = "Release research",
        CreatedAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 9, 1, 9, 5, 0, DateTimeKind.Utc),
        MessageCount = 4,
        ModelId = "llama3.2:3b",
        Messages =
        [
            Message(1, "user", "What changed in version 2?", 0),
            Message(2, "assistant", FirstAnswer, 1, "llama3.2:3b",
                Web("Release notes", "https://example.org/notes"),
                Web("Migration <guide>", "https://example.org/migrate?a=1&b=2")),
            Message(3, "user", "Who wrote about it?", 2),
            Message(4, "assistant", SecondAnswer, 3, "claude-sonnet-5",
                Web("Blog", "https://blog.example.org/v2")),
        ],
    };

    private static MessageEntity Message(
        long id, string role, string content, int sortOrder, string? modelId = null, params WebCitation[] sources) => new()
    {
        Id = id,
        ConversationId = 7,
        Role = role,
        Content = content,
        SortOrder = sortOrder,
        Timestamp = new DateTime(2026, 9, 1, 9, sortOrder, 0, DateTimeKind.Utc),
        ModelId = modelId,
        CitationsJson = MessageCitations.Serialize(sources),
    };

    private static WebCitation Web(string title, string url) => new()
    {
        Title = title,
        Url = url,
        Snippet = "Snippet.",
        Source = WebCitationSource.Web,
    };
}
