using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Indexing;

/// <summary>
/// Tests for <see cref="FileWatcherService"/> startup: the AutoIndexWatchFolders setting and
/// the catch-up scan that brings in files added or changed while the app was closed. The
/// document service is a mock; watch folders live in an in-memory database and point at a
/// real temporary directory.
/// </summary>
public sealed class FileWatcherServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Mock<IDocumentService> _documents = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly string _folder;
    private readonly List<FileWatcherService> _services = new();

    public FileWatcherServiceTests()
    {
        _db = _factory.CreateContext();
        _folder = Path.Combine(Path.GetTempPath(), "agentx-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _documents.Setup(d => d.CanProcess(It.IsAny<string>()))
            .Returns((string path) => Path.GetExtension(path) is ".txt" or ".md");
        _documents.Setup(d => d.ImportFileAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, long? _, CancellationToken _) => new DocumentEntity { Id = 1, FileName = Path.GetFileName(path) });
        SetAutoIndex(true);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        _db.Dispose();
        _factory.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task InitializeAsync_ImportsFilesAddedWhileTheAppWasClosed()
    {
        // The service was registered but never started, and nothing scanned the folders, so
        // files dropped in while the app was closed were never imported.
        var collectionId = SeedWatchFolder();
        var first = WriteFile("notes.txt", "first");
        var second = WriteFile(Path.Combine("sub", "plan.md"), "second");
        WriteFile("image.zzz", "unsupported");

        await NewService().InitializeAsync();

        _documents.Verify(d => d.ImportFileAsync(first, collectionId, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(d => d.ImportFileAsync(second, collectionId, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(d => d.ImportFileAsync(It.Is<string>(p => p.EndsWith(".zzz")), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_WhenAutoIndexIsOff_NeitherWatchesNorScans()
    {
        // The AutoIndexWatchFolders setting was saved but never read.
        SetAutoIndex(false);
        SeedWatchFolder();
        WriteFile("notes.txt", "content");

        var service = NewService();
        await service.InitializeAsync();

        service.IsWatching.Should().BeFalse();
        _documents.Verify(d => d.ImportFileAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CatchUpScan_LeavesUnchangedDocumentsAloneAndReindexesChangedOnes()
    {
        SeedWatchFolder();
        var unchanged = WriteFile("same.txt", "unchanged content");
        var changed = WriteFile("edited.txt", "edited content that grew");
        var unchangedId = SeedDocument(unchanged, new FileInfo(unchanged).Length);
        var changedId = SeedDocument(changed, fileSize: 3);

        await NewService().ScanWatchFoldersAsync();

        // A modified file refreshes its document instead of becoming a second document.
        _documents.Verify(d => d.ReindexDocumentAsync(changedId, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(d => d.ReindexDocumentAsync(unchangedId, It.IsAny<CancellationToken>()), Times.Never);
        _documents.Verify(d => d.ImportFileAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CatchUpScan_HonorsTheFolderExtensionFilterAndSubfolderSetting()
    {
        SeedWatchFolder(fileTypeFilter: "md", includeSubfolders: false);
        var markdown = WriteFile("top.md", "kept");
        WriteFile("top.txt", "filtered out by extension");
        WriteFile(Path.Combine("nested", "deep.md"), "filtered out by depth");

        var imported = await NewService().ScanWatchFoldersAsync();

        imported.Should().Be(1);
        _documents.Verify(d => d.ImportFileAsync(markdown, It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CatchUpScan_DuplicateContentIsSkippedQuietly()
    {
        SeedWatchFolder();
        WriteFile("copy.txt", "same bytes as an existing document");
        _documents.Setup(d => d.ImportFileAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateDocumentException(9, "x"));

        var imported = await NewService().ScanWatchFoldersAsync();

        imported.Should().Be(0);
    }

    // Helpers

    private FileWatcherService NewService()
    {
        var service = new FileWatcherService(_db, _documents.Object, new LoggerConfiguration().CreateLogger(), _settings.Object);
        _services.Add(service);
        return service;
    }

    private void SetAutoIndex(bool enabled)
        => _settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings { AutoIndexWatchFolders = enabled });

    private long SeedWatchFolder(string? fileTypeFilter = null, bool includeSubfolders = true)
    {
        var collection = new CollectionEntity { Name = "Watched", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Collections.Add(collection);
        _db.SaveChanges();

        _db.WatchFolders.Add(new WatchFolderEntity
        {
            FolderPath = _folder,
            IsEnabled = true,
            IncludeSubfolders = includeSubfolders,
            FileTypeFilter = fileTypeFilter,
            TargetCollectionId = collection.Id,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        return collection.Id;
    }

    private long SeedDocument(string path, long fileSize)
    {
        var document = new DocumentEntity
        {
            FileName = Path.GetFileName(path),
            FilePath = Path.GetFullPath(path),
            FileType = "txt",
            ContentHash = Guid.NewGuid().ToString("N"),
            FileSizeBytes = fileSize,
            ImportedAt = DateTime.UtcNow,
            FileModifiedAt = new FileInfo(path).LastWriteTimeUtc,
            IndexingStatus = "completed"
        };
        _db.Documents.Add(document);
        _db.SaveChanges();
        return document.Id;
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
