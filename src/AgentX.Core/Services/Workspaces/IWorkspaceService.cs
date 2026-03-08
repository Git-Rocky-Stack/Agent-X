using AgentX.Core.Services.Workspaces.Models;

namespace AgentX.Core.Services.Workspaces;

/// <summary>
/// Manages isolated workspace profiles, each with their own private storage directory
/// and SQLite database. One workspace is always active at a time.
/// </summary>
/// <remarks>
/// Workspace metadata (names, icons, colors, the active-workspace pointer) is persisted
/// to %LOCALAPPDATA%/AgentX/workspaces.json. Each workspace's data lives under
/// %LOCALAPPDATA%/AgentX/workspaces/{id}/. A built-in "Default" workspace is created
/// automatically on first use and can never be deleted.
/// </remarks>
public interface IWorkspaceService
{
    /// <summary>
    /// Returns all workspaces in creation order, including the built-in Default workspace.
    /// </summary>
    Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync();

    /// <summary>
    /// Creates a new workspace with the given display name and optional appearance values,
    /// then returns a <see cref="WorkspaceInfo"/> describing it.
    /// </summary>
    /// <param name="name">Non-empty display name for the workspace.</param>
    /// <param name="icon">
    /// Optional Segoe Fluent Icons glyph or emoji to represent the workspace.
    /// </param>
    /// <param name="color">Optional hex accent color (e.g. "#5E6AD2").</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is null or whitespace.
    /// </exception>
    Task<WorkspaceInfo> CreateWorkspaceAsync(string name, string? icon = null, string? color = null);

    /// <summary>
    /// Returns the workspace that is currently active. Initializes the Default
    /// workspace and marks it active if no workspace has been set yet.
    /// </summary>
    Task<WorkspaceInfo> GetActiveWorkspaceAsync();

    /// <summary>
    /// Makes the specified workspace the active one and persists the change.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace to activate.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no workspace with <paramref name="workspaceId"/> exists.
    /// </exception>
    Task SwitchWorkspaceAsync(long workspaceId);

    /// <summary>
    /// Renames an existing workspace.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace to rename.</param>
    /// <param name="newName">The replacement display name (must not be empty).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="newName"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the workspace does not exist or is the built-in Default workspace.
    /// </exception>
    Task RenameWorkspaceAsync(long workspaceId, string newName);

    /// <summary>
    /// Updates the icon and/or color of an existing workspace.
    /// Pass null to clear a previously set value.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace to update.</param>
    /// <param name="icon">New icon glyph, or null to clear it.</param>
    /// <param name="color">New hex color, or null to clear it.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no workspace with <paramref name="workspaceId"/> exists.
    /// </exception>
    Task UpdateWorkspaceAppearanceAsync(long workspaceId, string? icon, string? color);

    /// <summary>
    /// Permanently deletes a workspace and recursively removes its private
    /// storage directory and all data within it.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace to delete.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the workspace does not exist, is the built-in Default workspace,
    /// or is the currently active workspace.
    /// </exception>
    Task DeleteWorkspaceAsync(long workspaceId);

    /// <summary>
    /// Computes aggregate statistics (document count, conversation count, etc.) for
    /// the specified workspace by querying its private SQLite database directly.
    /// Returns zeroed stats when the workspace database does not yet exist.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace to query.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no workspace with <paramref name="workspaceId"/> exists.
    /// </exception>
    Task<WorkspaceStats> GetWorkspaceStatsAsync(long workspaceId);

    /// <summary>
    /// Returns the absolute path to a workspace's private storage directory
    /// (%LOCALAPPDATA%/AgentX/workspaces/{workspaceId}/).
    /// The directory is not guaranteed to exist until data is written to the workspace.
    /// </summary>
    /// <param name="workspaceId">ID of the workspace.</param>
    string GetWorkspaceStoragePath(long workspaceId);
}
