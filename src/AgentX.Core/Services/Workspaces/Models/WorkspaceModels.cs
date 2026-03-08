namespace AgentX.Core.Services.Workspaces.Models;

/// <summary>
/// Immutable snapshot of a workspace's identity, appearance, and current state.
/// Returned by all read operations on <see cref="IWorkspaceService"/>.
/// </summary>
public sealed record WorkspaceInfo
{
    /// <summary>Stable numeric identifier assigned at creation time.</summary>
    public required long Id { get; init; }

    /// <summary>User-visible display name of the workspace.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional Segoe Fluent Icons / emoji glyph used as the workspace icon.
    /// Null when no icon has been assigned.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Optional hex color string (e.g. "#5E6AD2") used to accent the workspace.
    /// Null when no color has been assigned.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// True for the built-in "Default" workspace that is seeded on first run
    /// and cannot be renamed or deleted.
    /// </summary>
    public required bool IsDefault { get; init; }

    /// <summary>True when this workspace is the currently active one.</summary>
    public required bool IsActive { get; init; }

    /// <summary>UTC timestamp when the workspace was first created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Absolute file-system path to the workspace's private storage directory
    /// (%LOCALAPPDATA%/AgentX/workspaces/{Id}/).
    /// </summary>
    public required string StoragePath { get; init; }
}

/// <summary>
/// Aggregate statistics computed from a workspace's private SQLite database.
/// All counts reflect the state of the database at the moment the query ran.
/// </summary>
public sealed record WorkspaceStats
{
    /// <summary>Total number of document records in the workspace database.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>Total number of non-archived conversation records.</summary>
    public required int ConversationCount { get; init; }

    /// <summary>Total number of collection records.</summary>
    public required int CollectionCount { get; init; }

    /// <summary>Total number of workflow records.</summary>
    public required int WorkflowCount { get; init; }

    /// <summary>
    /// Size of the workspace's SQLite database file on disk, expressed in megabytes.
    /// Zero when the database file does not yet exist.
    /// </summary>
    public required double DatabaseSizeMB { get; init; }
}
