using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBackingUp;
    [ObservableProperty] private bool _isRestoring;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Backup Options ───────────────────────────────────────
    [ObservableProperty] private string _backupDestination = string.Empty;
    [ObservableProperty] private string _encryptionPassword = string.Empty;
    [ObservableProperty] private bool _useEncryption;
    [ObservableProperty] private bool _includeDocuments = true;
    [ObservableProperty] private string _backupNotes = string.Empty;

    // ── Size Estimate ────────────────────────────────────────
    [ObservableProperty] private double _estimatedSizeMB;
    [ObservableProperty] private double _databaseSizeMB;
    [ObservableProperty] private double _documentsSizeMB;
    [ObservableProperty] private int _estimatedDocCount;
    [ObservableProperty] private bool _hasEstimate;

    // ── Progress ─────────────────────────────────────────────
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _progressPhase = string.Empty;
    [ObservableProperty] private string _progressItem = string.Empty;

    // ── Backup History ───────────────────────────────────────
    public ObservableCollection<BackupHistoryItem> BackupHistory { get; } = new();
    [ObservableProperty] private bool _hasHistory;

    // ── Restore ──────────────────────────────────────────────
    [ObservableProperty] private string _restoreFilePath = string.Empty;
    [ObservableProperty] private bool _restoreCompleted;
    [ObservableProperty] private string _restoreSummary = string.Empty;

    // ── Schedule ─────────────────────────────────────────────
    [ObservableProperty] private bool _scheduledBackupEnabled;
    [ObservableProperty] private int _scheduledIntervalHours = 168;
    [ObservableProperty] private int _maxBackupsToKeep = 5;

    /// <summary>Folder for scheduled backups; empty uses the Agent-X data folder.</summary>
    [ObservableProperty] private string _scheduledBackupDestination = string.Empty;
    [ObservableProperty] private bool _scheduledBackupUseEncryption;
    [ObservableProperty] private string _scheduledBackupPassword = string.Empty;
    [ObservableProperty] private bool _isSavingSchedule;
    [ObservableProperty] private string _scheduleStatusMessage = string.Empty;

    /// <summary>
    /// Raised when the backup being restored is encrypted. The view asks for the password and
    /// returns it, or null when the user cancels.
    /// </summary>
    public event Func<Task<string?>>? BackupPasswordRequested;

    public BackupRestoreViewModel(
        IBackupService backupService,
        ISettingsService settingsService,
        ILocalizationService localization)
    {
        _backupService = backupService;
        _settingsService = settingsService;
        _localization = localization;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Set default backup destination
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AgentX Backups");
            BackupDestination = defaultPath;

            // Load backup history
            await LoadBackupHistoryAsync();

            // Estimate backup size
            await EstimateBackupSizeAsync();

            // Show the saved backup schedule
            await LoadScheduleAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize BackupRestoreViewModel");
            StatusMessage = _localization.GetString("Backup_LoadFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBackupHistoryAsync()
    {
        try
        {
            var history = await _backupService.GetBackupHistoryAsync();
            var validLabel = _localization.GetString("Backup_IntegrityValid");
            var invalidLabel = _localization.GetString("Backup_IntegrityInvalid");
            BackupHistory.Clear();
            foreach (var backup in history)
            {
                BackupHistory.Add(new BackupHistoryItem
                {
                    Id = backup.Id,
                    FileName = backup.FileName,
                    FilePath = backup.FilePath,
                    BackupType = backup.BackupType,
                    BackupTypeLabel = DescribeBackupType(backup.BackupType),
                    SizeMB = backup.SizeMB,
                    CreatedAt = backup.CreatedAt,
                    Notes = backup.Notes ?? string.Empty,
                    ValidLabel = validLabel,
                    InvalidLabel = invalidLabel,
                    IsValid = backup.IsValid
                });
            }
            HasHistory = BackupHistory.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load backup history");
        }
    }

    /// <summary>
    /// The name shown for a stored backup type ("manual" or "scheduled"). Any other type is
    /// shown as stored.
    /// </summary>
    private string DescribeBackupType(string backupType) => backupType switch
    {
        "manual" => _localization.GetString("Backup_TypeManual"),
        "scheduled" => _localization.GetString("Backup_TypeScheduled"),
        _ => backupType
    };

    [RelayCommand]
    private async Task EstimateBackupSizeAsync()
    {
        try
        {
            var estimate = await _backupService.EstimateBackupSizeAsync();
            DatabaseSizeMB = estimate.DatabaseSizeMB;
            DocumentsSizeMB = estimate.DocumentsSizeMB;
            EstimatedSizeMB = estimate.TotalEstimatedMB;
            EstimatedDocCount = estimate.DocumentCount;
            HasEstimate = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to estimate backup size");
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupDestination))
        {
            StatusMessage = _localization.GetString("Backup_SelectDestination");
            return;
        }

        // An empty password used to produce an unencrypted backup although encryption was checked.
        if (UseEncryption && string.IsNullOrWhiteSpace(EncryptionPassword))
        {
            StatusMessage = _localization.GetString("Backup_PasswordRequired");
            return;
        }

        IsBackingUp = true;
        ProgressPercent = 0;
        ProgressPhase = _localization.GetString("Backup_PhasePreparing");
        ProgressItem = string.Empty;

        try
        {
            Directory.CreateDirectory(BackupDestination);

            var options = new BackupOptions
            {
                DestinationPath = BackupDestination,
                EncryptionPassword = UseEncryption ? EncryptionPassword : null,
                IncludeDocuments = IncludeDocuments,
                Notes = string.IsNullOrWhiteSpace(BackupNotes) ? null : BackupNotes,
                BackupType = "manual"
            };

            var progress = new Progress<BackupProgress>(p =>
            {
                ProgressPercent = p.PercentComplete;
                ProgressPhase = p.Phase;
                ProgressItem = p.CurrentItem ?? string.Empty;
            });

            var result = await _backupService.CreateBackupAsync(options, progress);

            if (result.Success)
            {
                var sizeMb = result.SizeMB.ToString("F1");
                var durationMs = result.DurationMs.ToString("F0");
                if (result.WarningMessages.Count > 0)
                {
                    var warnings = string.Join(" ", result.WarningMessages);
                    StatusMessage = _localization.GetString(
                        "Backup_CreatedWithWarnings", sizeMb, durationMs, result.WarningMessages.Count, warnings);
                }
                else
                {
                    StatusMessage = _localization.GetString("Backup_Created", sizeMb, durationMs);
                }

                await LoadBackupHistoryAsync();
            }
            else
            {
                StatusMessage = _localization.GetString("Backup_Failed", result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup creation failed");
            StatusMessage = _localization.GetString("Backup_Failed", ex.Message);
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RestoreFromBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(RestoreFilePath))
        {
            StatusMessage = _localization.GetString("Backup_SelectRestoreFile");
            return;
        }

        IsRestoring = true;
        RestoreCompleted = false;
        ProgressPercent = 0;
        ProgressPhase = _localization.GetString("Backup_PhaseValidating");

        try
        {
            var isValid = await _backupService.ValidateBackupAsync(RestoreFilePath);
            if (!isValid)
            {
                StatusMessage = _localization.GetString("Backup_InvalidFile");
                return;
            }

            // Encrypted archives are decrypted with the user's password before they are validated
            // and restored.
            string? password = null;
            if (await _backupService.IsEncryptedBackupAsync(RestoreFilePath))
            {
                password = BackupPasswordRequested is { } requestPassword
                    ? await requestPassword()
                    : null;

                if (string.IsNullOrEmpty(password))
                {
                    StatusMessage = _localization.GetString("Backup_RestorePasswordCancelled");
                    return;
                }
            }

            var progress = new Progress<BackupProgress>(p =>
            {
                ProgressPercent = p.PercentComplete;
                ProgressPhase = p.Phase;
                ProgressItem = p.CurrentItem ?? string.Empty;
            });

            var result = await _backupService.RestoreFromBackupAsync(RestoreFilePath, password, progress);

            if (result.Success)
            {
                RestoreCompleted = true;
                RestoreSummary = _localization.GetString(
                    "Backup_RestoreSummary",
                    result.RestoredConversationCount,
                    result.RestoredDocumentCount,
                    result.RestoredWorkflowCount,
                    result.DurationMs.ToString("F0"));
                // Search caches, vector indexes and open pages still hold the replaced data, and
                // the restored database's schema is upgraded at startup.
                StatusMessage = result.RequiresRestart
                    ? _localization.GetString("Backup_RestoreCompletedRestart")
                    : _localization.GetString("Backup_RestoreCompleted");

                if (result.WarningMessages.Count > 0)
                {
                    RestoreSummary += "\n\n" + _localization.GetString("Backup_RestoreWarningsHeading") + "\n" +
                                      string.Join("\n", result.WarningMessages.Select(w => $"  - {w}"));
                }
            }
            else
            {
                StatusMessage = _localization.GetString("Backup_RestoreFailed", result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
            StatusMessage = _localization.GetString("Backup_RestoreFailed", ex.Message);
        }
        finally
        {
            IsRestoring = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(long backupId)
    {
        try
        {
            await _backupService.DeleteBackupAsync(backupId);
            await LoadBackupHistoryAsync();
            StatusMessage = _localization.GetString("Backup_Deleted");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete backup {Id}", backupId);
            StatusMessage = _localization.GetString("Backup_DeleteFailed");
        }
    }

    [RelayCommand]
    private async Task RestoreFromHistoryAsync(string filePath)
    {
        RestoreFilePath = filePath;
        await RestoreFromBackupAsync();
    }

    // --- Schedule (AppSettings.BackupSchedule) ---

    /// <summary>Shows the schedule saved in settings (settings.json backupSchedule).</summary>
    private async Task LoadScheduleAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var schedule = settings.BackupSchedule ?? new BackupScheduleConfig();

            // A hand-edited file can hold values the page cannot show; BackupService clamps
            // them the same way when it runs the schedule.
            ScheduledBackupEnabled = schedule.Enabled;
            ScheduledIntervalHours = Math.Clamp(schedule.IntervalHours, 1, BackupScheduleConfig.MaxIntervalHours);
            MaxBackupsToKeep = Math.Max(0, schedule.MaxBackupsToKeep);
            ScheduledBackupDestination = schedule.DestinationPath ?? string.Empty;
            ScheduledBackupPassword = schedule.EncryptionPassword ?? string.Empty;
            ScheduledBackupUseEncryption = !string.IsNullOrEmpty(schedule.EncryptionPassword);
            ScheduleStatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the backup schedule");
        }
    }

    /// <summary>
    /// Saves the schedule through the settings service and applies it at once: an enabled
    /// schedule (re)starts the scheduled-backup loop with the saved values, a disabled one stops
    /// it. Nothing is saved when encryption is on without a password or the folder cannot be
    /// created, because every scheduled backup would then fail without telling anyone.
    /// </summary>
    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        var destination = ScheduledBackupDestination?.Trim() ?? string.Empty;
        var usePassword = ScheduledBackupUseEncryption;

        if (usePassword && string.IsNullOrWhiteSpace(ScheduledBackupPassword))
        {
            ScheduleStatusMessage = _localization.GetString("Backup_SchedulePasswordRequired");
            return;
        }

        IsSavingSchedule = true;
        try
        {
            if (destination.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(destination);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    ScheduleStatusMessage = _localization.GetString("Backup_ScheduleFolderInvalid", ex.Message);
                    return;
                }
            }

            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                settings.BackupSchedule = new BackupScheduleConfig
                {
                    Enabled = ScheduledBackupEnabled,
                    IntervalHours = ScheduledIntervalHours,
                    MaxBackupsToKeep = MaxBackupsToKeep,
                    DestinationPath = destination,
                    EncryptionPassword = usePassword ? ScheduledBackupPassword : null,
                };
                await _settingsService.SaveSettingsAsync(settings);
            }
            catch (SettingsValidationException ex)
            {
                ScheduleStatusMessage = _localization.GetString(
                    "Backup_ScheduleNotSaved", string.Join(" ", ex.Errors.Select(e => e.Message)));
                Log.Warning(ex, "The backup schedule was not saved because a value is invalid");
                return;
            }
            catch (Exception ex)
            {
                ScheduleStatusMessage = _localization.GetString("Backup_ScheduleNotSaved", ex.Message);
                Log.Error(ex, "Failed to save the backup schedule");
                return;
            }

            ScheduledBackupDestination = destination;

            try
            {
                if (ScheduledBackupEnabled)
                {
                    await _backupService.StartScheduledBackupsAsync();
                    ScheduleStatusMessage = _localization.GetString("Backup_ScheduleSavedOn");
                }
                else
                {
                    _backupService.StopScheduledBackups();
                    ScheduleStatusMessage = _localization.GetString("Backup_ScheduleSavedOff");
                }
            }
            catch (Exception ex)
            {
                ScheduleStatusMessage = _localization.GetString("Backup_ScheduleNotApplied", ex.Message);
                Log.Error(ex, "The backup schedule was saved but could not be applied");
            }
        }
        finally
        {
            IsSavingSchedule = false;
        }
    }
}

public partial class BackupHistoryItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _backupType = "manual";

    /// <summary>
    /// The backup type as shown in the history row, in the user's language. <see cref="BackupType"/>
    /// keeps the stored value.
    /// </summary>
    public string BackupTypeLabel { get; init; } = string.Empty;

    [ObservableProperty] private double _sizeMB;
    [ObservableProperty] private DateTime _createdAt;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntegrityLabel))]
    [NotifyPropertyChangedFor(nameof(IntegrityStatus))]
    private bool _isValid;

    /// <summary>Badge text for an intact backup, in the user's language.</summary>
    public string ValidLabel { get; init; } = string.Empty;

    /// <summary>Badge text for a backup that failed its integrity check, in the user's language.</summary>
    public string InvalidLabel { get; init; } = string.Empty;

    /// <summary>Human-readable integrity label for the history badge.</summary>
    public string IntegrityLabel => IsValid ? ValidLabel : InvalidLabel;

    /// <summary>
    /// Status token fed to StatusToColorConverter so the badge color reflects
    /// real integrity (completed = green, failed = red) instead of a constant green.
    /// </summary>
    public string IntegrityStatus => IsValid ? "completed" : "failed";
}
