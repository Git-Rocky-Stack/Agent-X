using AgentX.App.ViewModels;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// Watch folders had no UI: FileWatcherService could list, add and remove them, but nothing let
/// the user add one, and the Auto-index watch folders switch only applied at the next launch. The
/// Settings page's Watch Folders section lists, adds and removes folders and applies the saved
/// switch to the running service.
/// </summary>
public sealed class WatchFolderSettingsViewModelTests
{
    private readonly AppSettings _settings = new() { AutoIndexWatchFolders = true };
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IFileWatcherService> _watcher = new();
    private readonly Mock<ILocalizationService> _localization = new();
    private readonly List<WatchFolderEntity> _folders = new();

    public WatchFolderSettingsViewModelTests()
    {
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => _settings);
        _watcher.Setup(w => w.GetWatchFoldersAsync()).ReturnsAsync(() => _folders.ToList());
        _watcher.Setup(w => w.AddWatchFolderAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .Callback((string path, bool subfolders, string? _, long? _) => _folders.Add(new WatchFolderEntity
            {
                Id = _folders.Count + 1,
                FolderPath = path,
                IncludeSubfolders = subfolders,
                IsEnabled = true,
            }))
            .Returns(Task.CompletedTask);
        _watcher.Setup(w => w.RemoveWatchFolderAsync(It.IsAny<long>()))
            .Callback((long id) => _folders.RemoveAll(f => f.Id == id))
            .Returns(Task.CompletedTask);
        _watcher.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _watcher.Setup(w => w.StopWatchingAsync()).Returns(Task.CompletedTask);

        // Resource lookups come back as their keys and arguments.
        _localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" ", args)}");
    }

    private WatchFolderSettingsViewModel CreateSut() => new(_watcher.Object, _settingsService.Object, _localization.Object);

    private static string FolderPath(string name) => Path.Combine(Path.GetTempPath(), name);

    [Fact]
    public async Task LoadAsync_lists_the_folders_and_how_they_are_watched()
    {
        _folders.Add(new WatchFolderEntity { Id = 1, FolderPath = FolderPath("a"), IncludeSubfolders = true, FilesIndexed = 12 });
        _folders.Add(new WatchFolderEntity { Id = 2, FolderPath = FolderPath("b"), IncludeSubfolders = false, FilesIndexed = 0 });
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.HasFolders.Should().BeTrue();
        sut.Folders.Select(f => (f.Id, f.Path, f.Details)).Should().Equal(
            (1L, FolderPath("a"), "Settings_WatchFolderWithSubfolders: 12"),
            (2L, FolderPath("b"), "Settings_WatchFolderTopLevelOnly: 0"));
    }

    [Fact]
    public async Task AddFolder_while_the_switch_is_on_watches_it_and_imports_what_it_holds()
    {
        var sut = CreateSut();
        await sut.LoadAsync();
        sut.HasFolders.Should().BeFalse();

        await sut.AddFolderCommand.ExecuteAsync(FolderPath("docs"));
        await sut.CatchUp!;

        _watcher.Verify(w => w.AddWatchFolderAsync(FolderPath("docs"), true, null, null), Times.Once);
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the startup catch-up imports the supported files already in the folder");
        _watcher.Verify(w => w.StopWatchingAsync(), Times.Never);
        sut.Folders.Should().ContainSingle(f => f.Path == FolderPath("docs"));
        sut.HasFolders.Should().BeTrue();
        sut.StatusMessage.Should().Be($"Settings_WatchFolderAdded: {FolderPath("docs")}");
    }

    [Fact]
    public async Task AddFolder_while_the_switch_is_off_adds_it_without_watching()
    {
        _settings.AutoIndexWatchFolders = false;
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.AddFolderCommand.ExecuteAsync(FolderPath("docs"));

        _watcher.Verify(w => w.AddWatchFolderAsync(FolderPath("docs"), true, null, null), Times.Once);
        _watcher.Verify(w => w.StopWatchingAsync(), Times.Once, "the service starts watching every folder it adds");
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
        sut.CatchUp.Should().BeNull();
        sut.StatusMessage.Should().Be($"Settings_WatchFolderAddedPaused: {FolderPath("docs")}");
    }

    [Fact]
    public async Task AddFolder_passes_the_include_subfolders_choice()
    {
        var sut = CreateSut();
        await sut.LoadAsync();
        sut.IncludeSubfolders = false;

        await sut.AddFolderCommand.ExecuteAsync(FolderPath("top-only"));

        _watcher.Verify(w => w.AddWatchFolderAsync(FolderPath("top-only"), false, null, null), Times.Once);
        sut.Folders.Single().Details.Should().StartWith("Settings_WatchFolderTopLevelOnly");
    }

    [Fact]
    public async Task AddFolder_already_listed_is_reported_without_adding_it_again()
    {
        _folders.Add(new WatchFolderEntity { Id = 1, FolderPath = FolderPath("Docs"), IncludeSubfolders = true });
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.AddFolderCommand.ExecuteAsync(FolderPath("docs") + Path.DirectorySeparatorChar);

        _watcher.Verify(w => w.AddWatchFolderAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<long?>()), Times.Never);
        sut.StatusMessage.Should().StartWith("Settings_WatchFolderAlreadyAdded");
    }

    [Fact]
    public async Task AddFolder_that_no_longer_exists_is_reported()
    {
        _watcher.Setup(w => w.AddWatchFolderAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .ThrowsAsync(new DirectoryNotFoundException("gone"));
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.AddFolderCommand.ExecuteAsync(FolderPath("gone"));

        sut.StatusMessage.Should().Be($"Settings_WatchFolderNotFound: {FolderPath("gone")}");
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
        sut.HasFolders.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFolder_stops_watching_it_and_updates_the_list()
    {
        _folders.Add(new WatchFolderEntity { Id = 7, FolderPath = FolderPath("old"), IncludeSubfolders = true });
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.RemoveFolderCommand.ExecuteAsync(7L);

        _watcher.Verify(w => w.RemoveWatchFolderAsync(7), Times.Once);
        sut.Folders.Should().BeEmpty();
        sut.HasFolders.Should().BeFalse();
        sut.StatusMessage.Should().Be($"Settings_WatchFolderRemoved: {FolderPath("old")}");
    }

    [Fact]
    public async Task Saving_the_switch_off_stops_watching_at_once()
    {
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.ApplyAutoIndexAsync(false);

        _watcher.Verify(w => w.StopWatchingAsync(), Times.Once);
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_the_switch_on_starts_watching_and_catches_up_at_once()
    {
        _settings.AutoIndexWatchFolders = false;
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.ApplyAutoIndexAsync(true);
        await sut.CatchUp!;

        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        _watcher.Verify(w => w.StopWatchingAsync(), Times.Never);
    }

    [Fact]
    public async Task Saving_without_changing_the_switch_leaves_the_watcher_alone()
    {
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.ApplyAutoIndexAsync(true);

        sut.CatchUp.Should().BeNull("a save that did not change the switch must not rescan every folder");
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
        _watcher.Verify(w => w.StopWatchingAsync(), Times.Never);
    }

    [Fact]
    public async Task A_failed_catch_up_is_logged_not_thrown()
    {
        _watcher.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("disk"));
        _settings.AutoIndexWatchFolders = false;
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.ApplyAutoIndexAsync(true);

        var catchUp = () => sut.CatchUp!;
        await catchUp.Should().NotThrowAsync();
        _watcher.Verify(w => w.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
