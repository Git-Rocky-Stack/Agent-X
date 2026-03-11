namespace AgentX.Mobile.Models;

// ── Generic envelope ─────────────────────────────────────────────────────────

/// <summary>
/// Mirror of the desktop AgentX REST API response envelope.
/// Defined here so the mobile project has no compile-time dependency on
/// AgentX.Core (which targets a Windows-only TFM).
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public DateTime Timestamp { get; init; }
}

// ── Document ─────────────────────────────────────────────────────────────────

/// <summary>
/// A document as returned by GET /api/documents.
/// </summary>
public sealed class DocumentDto
{
    public long Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime ImportedAt { get; init; }
    public string IndexingStatus { get; init; } = string.Empty;

    // ── Derived display helpers ───────────────────────────────────────────────

    /// <summary>Human-readable file size (e.g., "1.4 MB").</summary>
    public string FileSizeDisplay => FileSizeBytes switch
    {
        >= 1_073_741_824 => $"{FileSizeBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{FileSizeBytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{FileSizeBytes / 1_024.0:F1} KB",
        _ => $"{FileSizeBytes} B"
    };

    /// <summary>Status label with an appropriate indicator character.</summary>
    public string StatusDisplay => IndexingStatus switch
    {
        "completed" => "Indexed",
        "processing" => "Processing...",
        "pending" => "Pending",
        "failed" => "Failed",
        _ => IndexingStatus
    };

    /// <summary>Color for the status badge.</summary>
    public Color StatusColor => IndexingStatus switch
    {
        "completed" => Color.FromArgb("#22C55E"),
        "processing" => Color.FromArgb("#F59E0B"),
        "pending" => Color.FromArgb("#94A3B8"),
        "failed" => Color.FromArgb("#EF4444"),
        _ => Color.FromArgb("#94A3B8")
    };
}

// ── Conversation ─────────────────────────────────────────────────────────────

/// <summary>
/// A conversation as returned by GET /api/conversations.
/// </summary>
public sealed class ConversationDto
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int MessageCount { get; init; }
    public long TokensUsed { get; init; }

    /// <summary>Short model name stripped of provider prefix.</summary>
    public string ModelDisplay => ModelId.Contains('/')
        ? ModelId[(ModelId.LastIndexOf('/') + 1)..]
        : ModelId;

    /// <summary>Relative time label for the last update.</summary>
    public string UpdatedDisplay
    {
        get
        {
            var diff = DateTime.UtcNow - UpdatedAt;
            return diff.TotalSeconds < 60 ? "Just now"
                : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes}m ago"
                : diff.TotalHours < 24 ? $"{(int)diff.TotalHours}h ago"
                : diff.TotalDays < 7 ? $"{(int)diff.TotalDays}d ago"
                : UpdatedAt.ToString("MMM d");
        }
    }
}

// ── Collection ────────────────────────────────────────────────────────────────

/// <summary>
/// A collection as returned by GET /api/collections.
/// </summary>
public sealed class CollectionDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DocumentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

// ── Search ────────────────────────────────────────────────────────────────────

/// <summary>
/// A single result from POST /api/search.
/// </summary>
public sealed class SearchResultDto
{
    public long DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ChunkContent { get; init; } = string.Empty;
    public float Score { get; init; }

    /// <summary>Relevance as a 0-100 integer percentage.</summary>
    public int RelevancePercent => (int)(Score * 100);

    /// <summary>Color graduated from red (low) to green (high).</summary>
    public Color RelevanceColor => Score switch
    {
        >= 0.8f => Color.FromArgb("#22C55E"),
        >= 0.6f => Color.FromArgb("#84CC16"),
        >= 0.4f => Color.FromArgb("#F59E0B"),
        _ => Color.FromArgb("#EF4444")
    };

    /// <summary>Content trimmed to a reasonable preview length.</summary>
    public string ContentPreview => ChunkContent.Length > 250
        ? string.Concat(ChunkContent.AsSpan(0, 247), "…")
        : ChunkContent;
}

// ── Health ────────────────────────────────────────────────────────────────────

/// <summary>
/// Response payload from GET /api/health.
/// </summary>
public sealed class HealthDto
{
    public string Status { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Uptime { get; init; } = string.Empty;
    public long DocumentCount { get; init; }
    public int ConversationCount { get; init; }
}
