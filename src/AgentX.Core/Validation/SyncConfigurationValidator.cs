using AgentX.Core.Constants;
using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Validation;

/// <summary>
/// Validates a <see cref="SyncConfiguration"/> instance against all known business rules
/// for the Collaborative Sync feature.
/// </summary>
/// <remarks>
/// <para>
/// This validator enforces the following constraints:
/// </para>
/// <list type="bullet">
///   <item><see cref="SyncConfiguration.SyncFolderPath"/> must not be empty and must be
///         a rooted (absolute) directory path.</item>
///   <item><see cref="SyncConfiguration.EncryptionKey"/> must not be empty and must
///         contain at least 8 characters.</item>
///   <item><see cref="SyncConfiguration.SyncIntervalMinutes"/> must be between 1 and
///         1440 (inclusive), representing up to 24 hours.</item>
///   <item>When <see cref="SyncConfiguration.SyncScope"/> is
///         <see cref="SyncScope.SelectedCollections"/>, the
///         <see cref="SyncConfiguration.SelectedCollectionIds"/> field must not be
///         null, empty, or whitespace.</item>
/// </list>
/// </remarks>
public sealed class SyncConfigurationValidator : IValidator<SyncConfiguration>
{
    private const int MinEncryptionKeyLength = AppConstants.MinEncryptionKeyLength;
    private const int MinSyncIntervalMinutes = AppConstants.MinSyncIntervalMinutes;
    private const int MaxSyncIntervalMinutes = AppConstants.MaxSyncIntervalMinutes;

    /// <inheritdoc />
    public ValidationResult Validate(SyncConfiguration instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var errors = new List<ValidationError>();

        // ── SyncFolderPath ───────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.SyncFolderPath))
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.SyncFolderPath),
                "Sync folder path must not be empty."));
        }
        else if (!Path.IsPathRooted(instance.SyncFolderPath))
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.SyncFolderPath),
                $"Sync folder path must be an absolute (rooted) path. Got '{instance.SyncFolderPath}'."));
        }

        // ── EncryptionKey ────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(instance.EncryptionKey))
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.EncryptionKey),
                "Encryption key must not be empty."));
        }
        else if (instance.EncryptionKey.Length < MinEncryptionKeyLength)
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.EncryptionKey),
                $"Encryption key must be at least {MinEncryptionKeyLength} characters long. Got {instance.EncryptionKey.Length} characters."));
        }

        // ── SyncIntervalMinutes ──────────────────────────────────────────
        if (instance.SyncIntervalMinutes < MinSyncIntervalMinutes ||
            instance.SyncIntervalMinutes > MaxSyncIntervalMinutes)
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.SyncIntervalMinutes),
                $"Sync interval must be between {MinSyncIntervalMinutes} and {MaxSyncIntervalMinutes} minutes. Got {instance.SyncIntervalMinutes}."));
        }

        // ── SelectedCollectionIds (conditional) ──────────────────────────
        if (instance.SyncScope == SyncScope.SelectedCollections &&
            string.IsNullOrWhiteSpace(instance.SelectedCollectionIds))
        {
            errors.Add(new ValidationError(
                nameof(SyncConfiguration.SelectedCollectionIds),
                "At least one collection ID must be specified when sync scope is 'SelectedCollections'."));
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
