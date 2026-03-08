using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Workspace;

/// <summary>
/// Defines the contract for managing <see cref="WorkspaceProfileEntity"/> records,
/// which capture a user-defined combination of active model, active collections,
/// and custom UI preferences that can be saved, loaded, and switched between at runtime.
/// </summary>
/// <remarks>
/// All methods are asynchronous and safe to call from the UI thread; they never
/// block on synchronous I/O.  Implementations must ensure that at most one profile
/// carries <see cref="WorkspaceProfileEntity.IsDefault"/> = <c>true</c> at any time.
/// </remarks>
public interface IWorkspaceProfileService
{
    /// <summary>
    /// Returns every saved workspace profile in ascending creation-date order.
    /// </summary>
    /// <returns>
    /// A read-only list of all profiles.  The list is empty when no profiles have
    /// been created yet (the default profile is created lazily on first use).
    /// </returns>
    Task<IReadOnlyList<WorkspaceProfileEntity>> GetAllProfilesAsync();

    /// <summary>
    /// Returns the workspace profile with the specified <paramref name="id"/>,
    /// or <c>null</c> when no matching record exists.
    /// </summary>
    /// <param name="id">The primary key of the profile to retrieve.</param>
    Task<WorkspaceProfileEntity?> GetProfileAsync(long id);

    /// <summary>
    /// Returns the profile that is currently designated as the default (the one
    /// that is loaded automatically on application start), or <c>null</c> when no
    /// profile has been marked as default yet.
    /// </summary>
    Task<WorkspaceProfileEntity?> GetDefaultProfileAsync();

    /// <summary>
    /// Creates a new workspace profile with the given <paramref name="name"/> and
    /// optional <paramref name="description"/>, persists it, and returns the
    /// fully-populated entity (including the generated <see cref="WorkspaceProfileEntity.Id"/>,
    /// <see cref="WorkspaceProfileEntity.CreatedAt"/>, and
    /// <see cref="WorkspaceProfileEntity.UpdatedAt"/> timestamps).
    /// </summary>
    /// <param name="name">
    /// Display name for the new profile.  Must not be null or whitespace.
    /// </param>
    /// <param name="description">
    /// Optional human-readable description of the profile's purpose.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is null or whitespace.
    /// </exception>
    Task<WorkspaceProfileEntity> CreateProfileAsync(string name, string? description = null);

    /// <summary>
    /// Persists all changes made to the provided <paramref name="profile"/> entity,
    /// updating <see cref="WorkspaceProfileEntity.UpdatedAt"/> to the current UTC time.
    /// </summary>
    /// <param name="profile">
    /// The profile entity to persist.  The entity must already exist in the database
    /// (i.e. <see cref="WorkspaceProfileEntity.Id"/> must be a valid primary key).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no profile with <see cref="WorkspaceProfileEntity.Id"/> exists in the store.
    /// </exception>
    Task UpdateProfileAsync(WorkspaceProfileEntity profile);

    /// <summary>
    /// Permanently removes the workspace profile identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The primary key of the profile to delete.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no profile with <paramref name="id"/> exists in the store.
    /// </exception>
    Task DeleteProfileAsync(long id);

    /// <summary>
    /// Marks the profile identified by <paramref name="id"/> as the default, and
    /// atomically clears the default flag on every other profile.
    /// </summary>
    /// <param name="id">The primary key of the profile to promote.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no profile with <paramref name="id"/> exists in the store.
    /// </exception>
    Task SetDefaultProfileAsync(long id);

    /// <summary>
    /// Creates a deep copy of the profile identified by <paramref name="sourceId"/>,
    /// assigns it the given <paramref name="newName"/>, resets
    /// <see cref="WorkspaceProfileEntity.IsDefault"/> to <c>false</c>, and returns
    /// the new persisted entity.
    /// </summary>
    /// <param name="sourceId">Primary key of the profile to clone.</param>
    /// <param name="newName">
    /// Display name for the duplicate.  Must not be null or whitespace.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no profile with <paramref name="sourceId"/> exists in the store.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="newName"/> is null or whitespace.
    /// </exception>
    Task<WorkspaceProfileEntity> DuplicateProfileAsync(long sourceId, string newName);
}
