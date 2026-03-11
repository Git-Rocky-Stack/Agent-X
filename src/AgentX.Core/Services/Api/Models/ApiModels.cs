namespace AgentX.Core.Services.Api.Models;

// ── Generic envelope ─────────────────────────────────────────────────────────

/// <summary>
/// Standard JSON envelope for every API response.
/// </summary>
/// <typeparam name="T">The payload type. Use <see cref="object"/> for void responses.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>True when the request was processed without errors.</summary>
    public bool Success { get; init; }

    /// <summary>The response payload. Null on error responses.</summary>
    public T? Data { get; init; }

    /// <summary>Human-readable error description. Null on success responses.</summary>
    public string? Error { get; init; }

    /// <summary>Server-side UTC timestamp when this response was generated.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // ── Factories ─────────────────────────────────────────────────────────────

    /// <summary>Creates a successful response wrapping <paramref name="data"/>.</summary>
    public static ApiResponse<T> Ok(T data) =>
        new() { Success = true, Data = data };

    /// <summary>Creates an error response with the supplied message.</summary>
    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Error = error };
}

// ── Document DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight document representation returned by the API.
/// </summary>
public sealed record ApiDocumentDto(
    long Id,
    string FileName,
    string FileType,
    long FileSizeBytes,
    DateTime ImportedAt,
    string IndexingStatus);

// ── Conversation DTOs ─────────────────────────────────────────────────────────

/// <summary>
/// Lightweight conversation representation returned by the API.
/// </summary>
public sealed record ApiConversationDto(
    long Id,
    string Title,
    string ModelId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    long TokensUsed);

// ── Collection DTOs ──────────────────────────────────────────────────────────

/// <summary>
/// Lightweight collection representation returned by the API.
/// </summary>
public sealed record ApiCollectionDto(
    long Id,
    string Name,
    string? Description,
    int DocumentCount,
    DateTime CreatedAt);

// ── Search DTOs ───────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/search.
/// </summary>
public sealed class ApiSearchRequest
{
    /// <summary>The natural language query string.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Maximum number of results to return. Defaults to 10.</summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum relevance score (0.0 – 1.0) for a result to be included.
    /// Defaults to 0.3.
    /// </summary>
    public float MinScore { get; init; } = 0.3f;
}

/// <summary>
/// A single semantic search result as surfaced by the API.
/// </summary>
public sealed record ApiSearchResultDto(
    long DocumentId,
    string FileName,
    string ChunkContent,
    float Score);

// ── Health DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// Response payload for GET /api/health.
/// </summary>
public sealed class ApiHealthDto
{
    /// <summary>Always "ok" when the host is reachable.</summary>
    public string Status { get; init; } = "ok";

    /// <summary>API version string.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Human-readable process uptime (e.g., "2h 15m 40s").</summary>
    public string Uptime { get; init; } = string.Empty;

    /// <summary>Total number of documents in the knowledge vault.</summary>
    public long DocumentCount { get; init; }

    /// <summary>Total number of non-archived conversations.</summary>
    public int ConversationCount { get; init; }
}
