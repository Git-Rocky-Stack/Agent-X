using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Workspace;

/// <summary>
/// Defines the contract for managing <see cref="WorkspaceProfileEntity"/> records,
/// which capture a user-defined combination of model identifier, collection IDs,
/// and custom settings.
/// </summary>
/// <remarks>
/// <para>
/// Profiles are saved presets only. Nothing in the application applies a profile:
/// selecting one, or marking one as the default, does not switch the active model,
/// the collections in scope, or any setting, and no profile is loaded at startup.
/// </para>
/// <para>
/// All methods are asynchronous and safe to call from the UI thread; they never
/// block on synchronous I/O.  Implementations must ensure that at most one profile
/// carries <see cref="WorkspaceProfileEntity.IsDefault"/> = <c>true</c> at any time.
/// </para>
/// </remarks>
public interface IWorkspaceProfileService
{
    /// <summary>
    /// Returns every saved workspace profile in ascending creation-date order.
    /// </summary>
    /// <returns>
    /// A read-only list of all profiles.  The list is empty until the user creates
    /// a profile; no profile is created automatically.
    /// </returns>
    Task<IReadOnlyList<WorkspaceProfileEntity>> GetAllProfilesAsync();

    /// <summary>
    /// Returns the workspace profile with the specified <paramref name="id"/>,
    /// or <c>null</c> when no matching record exists.
    /// </summary>
    /// <param name="id">The primary key of the profile to retrieve.</param>
    Task<WorkspaceProfileEntity?> GetProfileAsync(long id);

    /// <summary>
    /// Returns the profile that is currently marked as the default, or <c>null</c>
    /// when no profile carries the mark. The mark is informational: nothing loads
    /// the default profile at startup.
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
    /// Saves the editable fields of <paramref name="profile"/>
    /// (<see cref="WorkspaceProfileEntity.Name"/>, <see cref="WorkspaceProfileEntity.Description"/>,
    /// <see cref="WorkspaceProfileEntity.ActiveModelId"/>, <see cref="WorkspaceProfileEntity.ActiveCollectionIds"/>
    /// and <see cref="WorkspaceProfileEntity.CustomSettings"/>) and sets
    /// <see cref="WorkspaceProfileEntity.UpdatedAt"/> on the stored row and on
    /// <paramref name="profile"/> to the current UTC time. The instance may be detached
    /// (for example from <see cref="GetProfileAsync"/>) and may be saved any number of times.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkspaceProfileEntity.IsDefault"/> and <see cref="WorkspaceProfileEntity.CreatedAt"/>
    /// are not changed by this method; use <see cref="SetDefaultProfileAsync"/> or
    /// <see cref="ClearDefaultProfileAsync"/> to change the default flag.
    /// </remarks>
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
    /// Removes the default mark from the profile identified by <paramref name="id"/>,
    /// leaving no default profile. Does nothing when that profile is not the default.
    /// </summary>
    /// <param name="id">The primary key of the profile to demote.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no profile with <paramref name="id"/> exists in the store.
    /// </exception>
    Task ClearDefaultProfileAsync(long id);

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
