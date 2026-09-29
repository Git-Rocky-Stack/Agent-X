using System.Buffers;
using System.Text;
using System.Text.Json;
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

    /// <summary>
    /// Publication date of the original content, if available, exactly as the page states it.
    /// Kept as raw text on purpose: pages publish dates in many shapes ("+0000" offsets,
    /// "2024-03-05 10:00:00", "2024-03", "20240305"), and a strict DateTime binding turned every
    /// one of them into a 400 for the whole clip. The host parses it leniently and drops a value
    /// it cannot read.
    /// </summary>
    [JsonPropertyName("publishedDate")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? PublishedDate { get; init; }

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

/// <summary>
/// Response payload for GET /api/auth/check. Reaching the handler at all proves the bearer token
/// was accepted, so clients use this route to validate a token during pairing.
/// </summary>
public sealed class ApiAuthCheckDto
{
    /// <summary>Always true: an invalid token is rejected with 401 before the handler runs.</summary>
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; init; }

    /// <summary>AgentX application version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

/// <summary>
/// Reads a JSON string as-is, a JSON number as its literal text, and any other token (object,
/// array, boolean) as null. Used for optional free-text fields whose value is advisory, so a
/// malformed value degrades to "absent" instead of failing the whole request with a 400.
/// </summary>
internal sealed class LenientStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                return Encoding.UTF8.GetString(
                    reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);

            default:
                // Skip() moves past a whole object or array; for a scalar it is a no-op.
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
