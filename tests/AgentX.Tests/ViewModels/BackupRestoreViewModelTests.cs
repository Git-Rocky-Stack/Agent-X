using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using AgentX.Core.Validation;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// The backup schedule (AppSettings.BackupSchedule) had no controls: users were told to edit
/// settings.json while Agent-X was closed, and a change applied at the next launch. The Backup
/// and Restore page now loads it, saves it through the settings service and starts or stops the
/// scheduled-backup loop at once.
/// </summary>
public sealed class BackupRestoreViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agentx-bakvm-{Guid.NewGuid():N}");
    private readonly AppSettings _settings = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IBackupService> _backup = new();
    private readonly Mock<ILocalizationService> _localization = new();

    public BackupRestoreViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => _settings);
        _backup.Setup(b => b.GetBackupHistoryAsync()).ReturnsAsync(Array.Empty<BackupEntity>());
        _backup.Setup(b => b.EstimateBackupSizeAsync()).ReturnsAsync(new BackupSizeEstimate());

        // Resource lookups come back as their keys (and arguments), so tests read which message the
        // page shows without depending on the English text.
        _localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        _localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" ", args)}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private BackupRestoreViewModel CreateSut() => new(_backup.Object, _settingsService.Object, _localization.Object);

    [Fact]
    public async Task InitializeAsync_shows_the_saved_schedule()
    {
        _settings.BackupSchedule = new BackupScheduleConfig
        {
            Enabled = true,
            IntervalHours = 24,
            MaxBackupsToKeep = 9,
            DestinationPath = @"D:\AgentX Backups",
            EncryptionPassword = "s3cret",
        };
        var sut = CreateSut();

        await sut.InitializeAsync();

        sut.ScheduledBackupEnabled.Should().BeTrue();
        sut.ScheduledIntervalHours.Should().Be(24);
        sut.MaxBackupsToKeep.Should().Be(9);
        sut.ScheduledBackupDestination.Should().Be(@"D:\AgentX Backups");
        sut.ScheduledBackupUseEncryption.Should().BeTrue();
        sut.ScheduledBackupPassword.Should().Be("s3cret");
    }

    [Fact]
    public async Task InitializeAsync_clamps_hand_edited_values_the_page_cannot_show()
    {
        _settings.BackupSchedule = new BackupScheduleConfig { Enabled = true, IntervalHours = 0, MaxBackupsToKeep = -2 };
        var sut = CreateSut();

        await sut.InitializeAsync();

        sut.ScheduledIntervalHours.Should().Be(1);
        sut.MaxBackupsToKeep.Should().Be(0);
        sut.ScheduledBackupUseEncryption.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSchedule_persists_the_schedule_and_starts_it_at_once()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();
        var destination = Path.Combine(_root, "scheduled");
        sut.ScheduledBackupEnabled = true;
        sut.ScheduledIntervalHours = 48;
        sut.MaxBackupsToKeep = 3;
        sut.ScheduledBackupDestination = "  " + destination + "  ";
        sut.ScheduledBackupUseEncryption = true;
        sut.ScheduledBackupPassword = "pw";

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);
        _settings.BackupSchedule.Enabled.Should().BeTrue();
        _settings.BackupSchedule.IntervalHours.Should().Be(48);
        _settings.BackupSchedule.MaxBackupsToKeep.Should().Be(3);
        _settings.BackupSchedule.DestinationPath.Should().Be(destination);
        _settings.BackupSchedule.EncryptionPassword.Should().Be("pw");
        Directory.Exists(destination).Should().BeTrue("the folder is created when the schedule is saved");
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _backup.Verify(b => b.StopScheduledBackups(), Times.Never);
        sut.ScheduleStatusMessage.Should().Be("Backup_ScheduleSavedOn");
        sut.IsSavingSchedule.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSchedule_turned_off_persists_and_stops_the_running_schedule()
    {
        _settings.BackupSchedule = new BackupScheduleConfig { Enabled = true, IntervalHours = 24 };
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupEnabled = false;

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        _settings.BackupSchedule.Enabled.Should().BeFalse();
        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);
        _backup.Verify(b => b.StopScheduledBackups(), Times.Once);
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Never);
        sut.ScheduleStatusMessage.Should().Be("Backup_ScheduleSavedOff");
    }

    [Fact]
    public async Task SaveSchedule_with_an_empty_folder_keeps_the_default_destination()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupEnabled = true;
        sut.ScheduledBackupDestination = "   ";

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        _settings.BackupSchedule.DestinationPath.Should().BeEmpty("BackupService then writes to the Agent-X data folder");
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSchedule_without_encryption_drops_a_typed_password()
    {
        _settings.BackupSchedule = new BackupScheduleConfig { Enabled = true, EncryptionPassword = "old" };
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupUseEncryption = false;

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        _settings.BackupSchedule.EncryptionPassword.Should().BeNull();
    }

    [Fact]
    public async Task SaveSchedule_with_encryption_but_no_password_saves_nothing()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupEnabled = true;
        sut.ScheduledBackupUseEncryption = true;
        sut.ScheduledBackupPassword = " ";

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        sut.ScheduleStatusMessage.Should().Be("Backup_SchedulePasswordRequired");
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveSchedule_with_a_folder_that_cannot_be_created_saves_nothing()
    {
        var sut = CreateSut();
        await sut.InitializeAsync();
        var file = Path.Combine(_root, "a-file");
        await File.WriteAllTextAsync(file, "x");
        sut.ScheduledBackupEnabled = true;
        sut.ScheduledBackupDestination = Path.Combine(file, "backups");

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        sut.ScheduleStatusMessage.Should().StartWith("Backup_ScheduleFolderInvalid");
        _settingsService.Verify(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()), Times.Never);
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveSchedule_rejected_by_the_settings_service_does_not_start_the_schedule()
    {
        _settingsService.Setup(s => s.SaveSettingsAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new SettingsValidationException(new[]
            {
                new ValidationError("BackupSchedule.IntervalHours", "Backup interval must be between 1 and 720 hours. Got 0."),
            }));
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupEnabled = true;

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        sut.ScheduleStatusMessage.Should().Be("Backup_ScheduleNotSaved: Backup interval must be between 1 and 720 hours. Got 0.");
        _backup.Verify(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _backup.Verify(b => b.StopScheduledBackups(), Times.Never);
        sut.IsSavingSchedule.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSchedule_that_cannot_start_says_it_was_saved_but_not_applied()
    {
        _backup.Setup(b => b.StartScheduledBackupsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();
        await sut.InitializeAsync();
        sut.ScheduledBackupEnabled = true;

        await sut.SaveScheduleCommand.ExecuteAsync(null);

        _settingsService.Verify(s => s.SaveSettingsAsync(_settings), Times.Once);
        sut.ScheduleStatusMessage.Should().Be("Backup_ScheduleNotApplied: boom");
    }
}
