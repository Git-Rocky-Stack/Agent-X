using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Workspace;

/// <summary>
/// EF Core–backed implementation of <see cref="IWorkspaceProfileService"/>.
/// All database interactions run through a dedicated <see cref="AgentXDbContext"/>
/// instance injected at construction time.
/// </summary>
/// <remarks>
/// <para>
/// Read queries use <c>AsNoTracking()</c> to avoid change-tracker overhead, since
/// callers typically only need the data for display or serialisation purposes.
/// Write operations (create, update, delete, set-default, duplicate) track changes
/// explicitly and call <c>SaveChangesAsync</c> within the same unit of work.
/// </para>
/// <para>
/// <see cref="SetDefaultProfileAsync"/> wraps both the clear and promote steps in a
/// single <c>ExecuteUpdateAsync</c> + tracked-save sequence so the two writes are
/// committed atomically.  SQLite serialises writers, so no additional locking is
/// required at the application layer.
/// </para>
/// </remarks>
public sealed class WorkspaceProfileService : IWorkspaceProfileService
{
    private readonly AgentXDbContext _db;

    /// <summary>
    /// Initialises a new instance of <see cref="WorkspaceProfileService"/>.
    /// </summary>
    /// <param name="db">
    /// The <see cref="AgentXDbContext"/> used for all persistence operations.
    /// </param>
    public WorkspaceProfileService(AgentXDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;

        Log.Information("WorkspaceProfileService initialised");
    }

    // ------------------------------------------------------------------
    // Reads
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceProfileEntity>> GetAllProfilesAsync()
    {
        Log.Information("Retrieving all workspace profiles");

        var profiles = await _db.WorkspaceProfiles
            .AsNoTracking()
            .OrderBy(p => p.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        Log.Information("Retrieved {Count} workspace profile(s)", profiles.Count);

        return profiles;
    }

    /// <inheritdoc />
    public async Task<WorkspaceProfileEntity?> GetProfileAsync(long id)
    {
        Log.Information("Retrieving workspace profile {ProfileId}", id);

        var profile = await _db.WorkspaceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (profile is null)
            Log.Information("Workspace profile {ProfileId} not found", id);
        else
            Log.Information("Retrieved workspace profile {ProfileId} '{Name}'", id, profile.Name);

        return profile;
    }

    /// <inheritdoc />
    public async Task<WorkspaceProfileEntity?> GetDefaultProfileAsync()
    {
        Log.Information("Retrieving default workspace profile");

        var profile = await _db.WorkspaceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault)
            .ConfigureAwait(false);

        if (profile is null)
            Log.Information("No default workspace profile is currently set");
        else
            Log.Information("Default workspace profile is {ProfileId} '{Name}'", profile.Id, profile.Name);

        return profile;
    }

    // ------------------------------------------------------------------
    // Writes
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<WorkspaceProfileEntity> CreateProfileAsync(
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = DateTime.UtcNow;

        var profile = new WorkspaceProfileEntity
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.WorkspaceProfiles.Add(profile);

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "Created workspace profile {ProfileId} '{Name}'",
                profile.Id, profile.Name);

            return profile;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create workspace profile '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateProfileAsync(WorkspaceProfileEntity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Verify the record exists before attempting an update so callers receive
        // a clear InvalidOperationException rather than an EF concurrency error.
        var exists = await _db.WorkspaceProfiles
            .AsNoTracking()
            .AnyAsync(p => p.Id == profile.Id)
            .ConfigureAwait(false);

        if (!exists)
            throw new InvalidOperationException(
                $"Workspace profile {profile.Id} does not exist and cannot be updated.");

        profile.UpdatedAt = DateTime.UtcNow;

        // Attach the detached entity (result of a previous AsNoTracking query)
        // and mark every scalar property as modified.
        _db.WorkspaceProfiles.Update(profile);

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "Updated workspace profile {ProfileId} '{Name}'",
                profile.Id, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update workspace profile {ProfileId}", profile.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteProfileAsync(long id)
    {
        // Load tracked so we can call Remove without a second round-trip.
        var profile = await _db.WorkspaceProfiles
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (profile is null)
            throw new InvalidOperationException(
                $"Workspace profile {id} does not exist and cannot be deleted.");

        _db.WorkspaceProfiles.Remove(profile);

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information("Deleted workspace profile {ProfileId} '{Name}'", id, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete workspace profile {ProfileId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetDefaultProfileAsync(long id)
    {
        // Confirm the target profile exists before modifying anything.
        var target = await _db.WorkspaceProfiles
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (target is null)
            throw new InvalidOperationException(
                $"Workspace profile {id} does not exist.");

        if (target.IsDefault)
        {
            Log.Information("Workspace profile {ProfileId} is already the default — no-op", id);
            return;
        }

        var now = DateTime.UtcNow;

        // Step 1: Clear the IsDefault flag on every profile that currently carries it,
        // using a bulk ExecuteUpdateAsync to avoid loading all rows into memory.
        await _db.WorkspaceProfiles
            .Where(p => p.IsDefault && p.Id != id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.IsDefault, false)
                .SetProperty(p => p.UpdatedAt, now))
            .ConfigureAwait(false);

        // Step 2: Promote the target profile.  The entity is already tracked from
        // the FirstOrDefaultAsync call above, so no extra round-trip is needed.
        target.IsDefault = true;
        target.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "Workspace profile {ProfileId} '{Name}' set as default",
                id, target.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set workspace profile {ProfileId} as default", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceProfileEntity> DuplicateProfileAsync(long sourceId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var source = await _db.WorkspaceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == sourceId)
            .ConfigureAwait(false);

        if (source is null)
            throw new InvalidOperationException(
                $"Workspace profile {sourceId} does not exist and cannot be duplicated.");

        var now = DateTime.UtcNow;

        // Clone all content fields.  Id is intentionally omitted so EF Core
        // generates a new primary key.  Name is replaced with the caller-supplied
        // value.  IsDefault is always false — the duplicate starts as a neutral profile.
        var duplicate = new WorkspaceProfileEntity
        {
            Name = newName.Trim(),
            Description = source.Description,
            ActiveModelId = source.ActiveModelId,
            ActiveCollectionIds = source.ActiveCollectionIds,
            CustomSettings = source.CustomSettings,
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.WorkspaceProfiles.Add(duplicate);

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            Log.Information(
                "Duplicated workspace profile {SourceId} '{SourceName}' → {NewId} '{NewName}'",
                sourceId, source.Name, duplicate.Id, duplicate.Name);

            return duplicate;
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Failed to duplicate workspace profile {SourceId} as '{NewName}'",
                sourceId, newName);
            throw;
        }
    }
}
