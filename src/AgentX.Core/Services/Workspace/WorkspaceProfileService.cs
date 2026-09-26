using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Workspace;

/// <summary>
/// EF Core backed implementation of <see cref="IWorkspaceProfileService"/>.
/// All database interactions run through the <see cref="AgentXDbContext"/>
/// instance injected at construction time.
/// </summary>
/// <remarks>
/// <para>
/// Read queries use <c>AsNoTracking()</c> to avoid change-tracker overhead, since
/// callers typically only need the data for display or serialisation purposes.
/// </para>
/// <para>
/// The injected context is the application's long-lived shared context, so an entity
/// tracked by one call is still tracked on the next. Writes therefore never attach a
/// caller's detached instance (a second save of the same profile would collide with the
/// instance tracked by the first); <see cref="UpdateProfileAsync"/> copies the editable
/// fields onto the tracked instance instead. <see cref="SetDefaultProfileAsync"/> and
/// <see cref="ClearDefaultProfileAsync"/> change the default flag with a single
/// <c>ExecuteUpdateAsync</c> statement, which is atomic but bypasses the change
/// tracker, so they reload every tracked profile afterwards. Otherwise identity
/// resolution would keep handing out the old <see cref="WorkspaceProfileEntity.IsDefault"/>
/// value.
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
        {
            DetachTracked(profile.Id);
            throw new InvalidOperationException(
                $"Workspace profile {profile.Id} does not exist and cannot be updated.");
        }

        // Update the instance the shared context already tracks (or load it). Attaching the
        // caller's instance with Update() threw "another instance with the same key value is
        // already being tracked" on the second save of the same profile.
        var tracked = await _db.WorkspaceProfiles
            .FindAsync(profile.Id)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Workspace profile {profile.Id} does not exist and cannot be updated.");

        var now = DateTime.UtcNow;

        tracked.Name = profile.Name;
        tracked.Description = profile.Description;
        tracked.ActiveModelId = profile.ActiveModelId;
        tracked.ActiveCollectionIds = profile.ActiveCollectionIds;
        tracked.CustomSettings = profile.CustomSettings;
        tracked.UpdatedAt = now;

        // IsDefault and CreatedAt are not edited here: the default flag changes only through
        // SetDefaultProfileAsync and ClearDefaultProfileAsync, which keep at most one default.
        // If the caller passed the tracked instance itself with either value edited, put the
        // stored value back.
        var entry = _db.Entry(tracked);
        entry.Property(p => p.IsDefault).CurrentValue = entry.Property(p => p.IsDefault).OriginalValue;
        entry.Property(p => p.CreatedAt).CurrentValue = entry.Property(p => p.CreatedAt).OriginalValue;

        try
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);

            profile.UpdatedAt = now;

            Log.Information(
                "Updated workspace profile {ProfileId} '{Name}'",
                profile.Id, tracked.Name);
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
        await EnsureExistsAsync(id).ConfigureAwait(false);

        var now = DateTime.UtcNow;

        try
        {
            // One UPDATE statement promotes the target and clears every other default, so the
            // at-most-one-default rule is never half applied. It tests the stored flag, not a
            // tracked instance: the old code read IsDefault = true left over on a tracked
            // instance from an earlier call (SetDefault A, B, A) and skipped the promotion.
            var changed = await _db.WorkspaceProfiles
                .Where(p => p.IsDefault != (p.Id == id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.IsDefault, p => p.Id == id)
                    .SetProperty(p => p.UpdatedAt, now))
                .ConfigureAwait(false);

            await ReloadTrackedProfilesAsync().ConfigureAwait(false);

            if (changed == 0)
                Log.Information("Workspace profile {ProfileId} is already the default", id);
            else
                Log.Information("Workspace profile {ProfileId} set as default", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set workspace profile {ProfileId} as default", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ClearDefaultProfileAsync(long id)
    {
        await EnsureExistsAsync(id).ConfigureAwait(false);

        var now = DateTime.UtcNow;

        try
        {
            var changed = await _db.WorkspaceProfiles
                .Where(p => p.Id == id && p.IsDefault)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.IsDefault, false)
                    .SetProperty(p => p.UpdatedAt, now))
                .ConfigureAwait(false);

            await ReloadTrackedProfilesAsync().ConfigureAwait(false);

            if (changed == 0)
                Log.Information("Workspace profile {ProfileId} was not the default", id);
            else
                Log.Information("Workspace profile {ProfileId} is no longer the default", id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear the default flag on workspace profile {ProfileId}", id);
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

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task EnsureExistsAsync(long id)
    {
        var exists = await _db.WorkspaceProfiles
            .AsNoTracking()
            .AnyAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (!exists)
        {
            DetachTracked(id);
            throw new InvalidOperationException($"Workspace profile {id} does not exist.");
        }
    }

    /// <summary>
    /// Re-reads every tracked profile after a bulk update. ExecuteUpdateAsync writes straight
    /// to the database, and the shared context would otherwise keep serving the old values to
    /// tracked queries (identity resolution) and to the next save.
    /// </summary>
    private async Task ReloadTrackedProfilesAsync()
    {
        var entries = _db.ChangeTracker.Entries<WorkspaceProfileEntity>().ToList();
        foreach (var entry in entries)
            await entry.ReloadAsync().ConfigureAwait(false);
    }

    /// <summary>Stops tracking a profile whose row no longer exists.</summary>
    private void DetachTracked(long id)
    {
        var entry = _db.ChangeTracker.Entries<WorkspaceProfileEntity>()
            .FirstOrDefault(e => e.Entity.Id == id);
        if (entry is not null)
            entry.State = EntityState.Detached;
    }
}
