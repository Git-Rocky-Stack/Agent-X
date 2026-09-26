using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Backup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly IBackupService _backupService;

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

    /// <summary>
    /// Raised when the backup being restored is encrypted. The view asks for the password and
    /// returns it, or null when the user cancels.
    /// </summary>
    public event Func<Task<string?>>? BackupPasswordRequested;

    public BackupRestoreViewModel(IBackupService backupService)
    {
        _backupService = backupService;
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize BackupRestoreViewModel");
            StatusMessage = "Failed to load backup information";
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
            BackupHistory.Clear();
            foreach (var backup in history)
            {
                BackupHistory.Add(new BackupHistoryItem
                {
                    Id = backup.Id,
                    FileName = backup.FileName,
                    FilePath = backup.FilePath,
                    BackupType = backup.BackupType,
                    SizeMB = backup.SizeMB,
                    CreatedAt = backup.CreatedAt,
                    Notes = backup.Notes ?? string.Empty,
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
            StatusMessage = "Please select a backup destination";
            return;
        }

        // An empty password used to produce an unencrypted backup although encryption was checked.
        if (UseEncryption && string.IsNullOrWhiteSpace(EncryptionPassword))
        {
            StatusMessage = "Enter a password to encrypt the backup, or turn encryption off.";
            return;
        }

        IsBackingUp = true;
        ProgressPercent = 0;
        ProgressPhase = "Preparing...";
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
                StatusMessage = $"Backup created successfully ({result.SizeMB:F1} MB, {result.DurationMs:F0}ms)";
                if (result.WarningMessages.Count > 0)
                {
                    StatusMessage += $" with {result.WarningMessages.Count} warning(s): " +
                                     string.Join(" ", result.WarningMessages);
                }

                await LoadBackupHistoryAsync();
            }
            else
            {
                StatusMessage = $"Backup failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup creation failed");
            StatusMessage = $"Backup failed: {ex.Message}";
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
            StatusMessage = "Please select a backup file to restore";
            return;
        }

        IsRestoring = true;
        RestoreCompleted = false;
        ProgressPercent = 0;
        ProgressPhase = "Validating...";

        try
        {
            var isValid = await _backupService.ValidateBackupAsync(RestoreFilePath);
            if (!isValid)
            {
                StatusMessage = "Invalid or corrupted backup file";
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
                    StatusMessage = "Restore cancelled: this backup is encrypted and needs its password.";
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
                RestoreSummary = $"Restored {result.RestoredConversationCount} conversations, " +
                                 $"{result.RestoredDocumentCount} documents, " +
                                 $"{result.RestoredWorkflowCount} workflows " +
                                 $"in {result.DurationMs:F0}ms";
                // Search caches, vector indexes and open pages still hold the replaced data, and
                // the restored database's schema is upgraded at startup.
                StatusMessage = result.RequiresRestart
                    ? "Restore completed. Restart Agent-X now to load the restored data."
                    : "Restore completed successfully.";

                if (result.WarningMessages.Count > 0)
                {
                    RestoreSummary += "\n\nWarnings:\n" + string.Join("\n", result.WarningMessages.Select(w => $"  - {w}"));
                }
            }
            else
            {
                StatusMessage = $"Restore failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
            StatusMessage = $"Restore failed: {ex.Message}";
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
            StatusMessage = "Backup deleted";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete backup {Id}", backupId);
            StatusMessage = "Failed to delete backup";
        }
    }

    [RelayCommand]
    private async Task RestoreFromHistoryAsync(string filePath)
    {
        RestoreFilePath = filePath;
        await RestoreFromBackupAsync();
    }
}

public partial class BackupHistoryItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _backupType = "manual";
    [ObservableProperty] private double _sizeMB;
    [ObservableProperty] private DateTime _createdAt;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntegrityLabel))]
    [NotifyPropertyChangedFor(nameof(IntegrityStatus))]
    private bool _isValid;

    /// <summary>Human-readable integrity label for the history badge.</summary>
    public string IntegrityLabel => IsValid ? "Valid" : "Invalid";

    /// <summary>
    /// Status token fed to StatusToColorConverter so the badge color reflects
    /// real integrity (completed = green, failed = red) instead of a constant green.
    /// </summary>
    public string IntegrityStatus => IsValid ? "completed" : "failed";
}
