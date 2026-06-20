using System.Text.Json.Serialization;

namespace AgentX.Core.Services.Api.Models;

/// <summary>
/// Request body for POST /api/inbox/clip.
/// Sent by the AgentX browser extension to clip web content into the Smart Inbox.
/// </summary>
public sealed class ApiClipRequest
{
    /// <summary>Title or headline of the clipped content.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>The clipped text content (markdown or plain text).</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>The source URL the content was clipped from.</summary>
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>Author of the original content, if available.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Publication date of the original content, if available.</summary>
    [JsonPropertyName("publishedDate")]
    public DateTime? PublishedDate { get; init; }

    /// <summary>
    /// How the content was captured: "full" (entire page), "selection" (user selection),
    /// or "reader" (reader-mode extraction).
    /// </summary>
    [JsonPropertyName("clipMode")]
    public string ClipMode { get; init; } = "selection";

    /// <summary>Word count of the clipped content.</summary>
    [JsonPropertyName("wordCount")]
    public int WordCount { get; init; }

    /// <summary>Optional metadata key-value pairs for extensibility.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Response payload for POST /api/inbox/clip.
/// </summary>
public sealed class ApiClipResponse
{
    /// <summary>ID of the newly created inbox item.</summary>
    [JsonPropertyName("inboxItemId")]
    public long InboxItemId { get; init; }

    /// <summary>Status of the operation (e.g., "clipped", "duplicate").</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Human-readable message describing the outcome.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Response payload for GET /api/extension/health.
/// Lets the browser extension verify Agent-X is running and capable.
/// </summary>
public sealed class ApiExtensionHealthDto
{
    /// <summary>Whether Agent-X is connected and operational.</summary>
    [JsonPropertyName("connected")]
    public bool Connected { get; init; }

    /// <summary>AgentX application version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Whether the Smart Inbox feature is enabled and available.</summary>
    [JsonPropertyName("inboxEnabled")]
    public bool InboxEnabled { get; init; }

    /// <summary>The AI provider currently configured (e.g., "ollama", "openai").</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;
}
