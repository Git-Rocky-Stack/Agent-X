using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The Watch Folders section of Settings, next to the Auto-index watch folders switch. Lists the
/// folders <see cref="IFileWatcherService"/> monitors, adds and removes them, and applies the
/// switch to the running service when settings are saved, so neither waits for the next launch.
/// <para>
/// The switch is the saved setting (AppSettings.AutoIndexWatchFolders). While it is on, adding a
/// folder runs the same catch-up as startup, so the supported files already in the folder are
/// imported, and turning it on starts watching and catches up; while it is off, nothing is watched
/// or imported, also right after a folder is added.
/// </para>
/// </summary>
public sealed partial class WatchFolderSettingsViewModel : ObservableObject
{
    private readonly IFileWatcherService _fileWatcher;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;

    /// <summary>The switch value last applied to the service; null until loaded.</summary>
    private bool? _appliedAutoIndex;

    public ObservableCollection<WatchFolderItem> Folders { get; } = new();

    [ObservableProperty] private bool _hasFolders;

    /// <summary>Whether the next folder added includes its subfolders.</summary>
    [ObservableProperty] private bool _includeSubfolders = true;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public WatchFolderSettingsViewModel(
        IFileWatcherService fileWatcher,
        ISettingsService settingsService,
        ILocalizationService localization)
    {
        _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    /// <summary>
    /// The background catch-up started last (watching every folder and importing what is new or
    /// changed), or null. Exposed so tests can wait for it; failures are logged, never thrown.
    /// </summary>
    internal Task? CatchUp { get; private set; }

    /// <summary>Loads the folder list and the saved state of the switch.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            _appliedAutoIndex = settings.AutoIndexWatchFolders;
            await RefreshFoldersAsync();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load the watch folders");
            StatusMessage = _localization.GetString("Settings_WatchFoldersLoadFailed", ex.Message);
        }
    }

    /// <summary>
    /// Adds <paramref name="path"/> (chosen with the folder picker) as a watch folder, including
    /// subfolders when <see cref="IncludeSubfolders"/> is on.
    /// </summary>
    [RelayCommand]
    private async Task AddFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        // Windows paths are case-insensitive; the service compares them exactly.
        if (Folders.Any(f => SamePath(f.Path, path)))
        {
            StatusMessage = _localization.GetString("Settings_WatchFolderAlreadyAdded", path);
            return;
        }

        try
        {
            await _fileWatcher.AddWatchFolderAsync(path, IncludeSubfolders);
        }
        catch (DirectoryNotFoundException)
        {
            StatusMessage = _localization.GetString("Settings_WatchFolderNotFound", path);
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add watch folder {Path}", path);
            StatusMessage = _localization.GetString("Settings_WatchFolderAddFailed", ex.Message);
            return;
        }

        // The service starts watching a folder as soon as it is added. That matches the switch
        // only while it is on, and then the files already in the folder still need importing.
        if (await IsAutoIndexOnAsync())
        {
            StartCatchUp();
            StatusMessage = _localization.GetString("Settings_WatchFolderAdded", path);
        }
        else
        {
            await StopWatchingAsync();
            StatusMessage = _localization.GetString("Settings_WatchFolderAddedPaused", path);
        }

        await RefreshFoldersAsync();
    }

    /// <summary>Stops watching a folder and forgets it. Documents imported from it stay in the vault.</summary>
    [RelayCommand]
    private async Task RemoveFolderAsync(long watchFolderId)
    {
        var path = Folders.FirstOrDefault(f => f.Id == watchFolderId)?.Path ?? string.Empty;
        try
        {
            await _fileWatcher.RemoveWatchFolderAsync(watchFolderId);
            StatusMessage = _localization.GetString("Settings_WatchFolderRemoved", path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to remove watch folder {Id}", watchFolderId);
            StatusMessage = _localization.GetString("Settings_WatchFolderRemoveFailed", ex.Message);
        }

        await RefreshFoldersAsync();
    }

    /// <summary>
    /// Applies the saved Auto-index watch folders switch to the running service: on starts watching
    /// and catches up in the background, off stops watching. Called after settings are saved; does
    /// nothing when the switch did not change.
    /// </summary>
    public async Task ApplyAutoIndexAsync(bool enabled)
    {
        if (_appliedAutoIndex == enabled)
            return;

        _appliedAutoIndex = enabled;
        if (enabled)
            StartCatchUp();
        else
            await StopWatchingAsync();
    }

    private async Task<bool> IsAutoIndexOnAsync()
    {
        if (_appliedAutoIndex is { } applied)
            return applied;

        try
        {
            return (await _settingsService.GetSettingsAsync()).AutoIndexWatchFolders;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the AutoIndexWatchFolders setting; treating it as off");
            return false;
        }
    }

    /// <summary>
    /// Starts watching every folder and imports the files that are new or changed, like startup.
    /// On the thread pool: a folder with many files takes a while, and saving settings must not
    /// wait for it. The service reads the saved switch itself and does nothing when it is off.
    /// </summary>
    private void StartCatchUp()
    {
        CatchUp = Task.Run(async () =>
        {
            try
            {
                await _fileWatcher.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Watch folder catch-up failed");
            }
        });
    }

    private async Task StopWatchingAsync()
    {
        try
        {
            await _fileWatcher.StopWatchingAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not stop watching folders");
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task RefreshFoldersAsync()
    {
        IReadOnlyList<WatchFolderEntity> folders;
        try
        {
            folders = await _fileWatcher.GetWatchFoldersAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to list the watch folders");
            return;
        }

        Folders.Clear();
        foreach (var folder in folders)
        {
            var details = folder.IncludeSubfolders
                ? _localization.GetString("Settings_WatchFolderWithSubfolders", folder.FilesIndexed)
                : _localization.GetString("Settings_WatchFolderTopLevelOnly", folder.FilesIndexed);
            Folders.Add(new WatchFolderItem(folder.Id, folder.FolderPath, details));
        }

        HasFolders = Folders.Count > 0;
    }
}

/// <summary>One row of the Watch Folders list.</summary>
public sealed class WatchFolderItem
{
    public WatchFolderItem(long id, string path, string details)
    {
        Id = id;
        Path = path;
        Details = details;
    }

    public long Id { get; }

    public string Path { get; }

    /// <summary>Whether subfolders are watched, and how many files were imported from the folder.</summary>
    public string Details { get; }
}
