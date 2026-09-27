using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Web;
using AgentX.Core.Services.Web.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Web;

public sealed class WebImportServiceTests : IDisposable
{
    private readonly string _storageDir = Path.Combine(Path.GetTempPath(), "agentx-webimport-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IWebScraperService> _scraper = new();
    private readonly Mock<ISettingsService> _settings = new();

    public WebImportServiceTests()
    {
        _settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new AppSettings { StoragePath = _storageDir });
        _scraper.Setup(s => s.IsValidUrl(It.IsAny<string>()))
            .Returns((string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        _scraper.Setup(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => url.Contains("broken", StringComparison.Ordinal)
                ? new WebContent { Url = url, Success = false, ErrorMessage = "HTTP 404" }
                : new WebContent
                {
                    Url = url,
                    Title = "Page " + url[(url.LastIndexOf('/') + 1)..],
                    Content = "Body of " + url,
                    WordCount = 3,
                    Success = true,
                });
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageDir))
        {
            Directory.Delete(_storageDir, recursive: true);
        }
    }

    private WebImportService CreateService(AgentXDbContext db) =>
        CreateService(db, CreateDocumentService(db));

    private WebImportService CreateService(AgentXDbContext db, IDocumentService documentService) =>
        new(_scraper.Object, db, documentService, _settings.Object, Logger.None);

    private DocumentService CreateDocumentService(AgentXDbContext db) =>
        new(db, Array.Empty<IDocumentProcessor>(), _settings.Object, Logger.None);

    [Fact]
    public async Task ImportFromUrlsAsync_returns_one_result_per_url_in_order_so_a_failure_never_shifts_documents()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);

        var results = await service.ImportFromUrlsAsync(
            ["https://example.com/first", "https://example.com/broken", "https://example.com/third"]);

        results.Select(r => r.Url).Should().Equal(
            "https://example.com/first", "https://example.com/broken", "https://example.com/third");
        results[0].Success.Should().BeTrue();
        results[0].Document!.ExtractedTitle.Should().Be("Page first");
        results[1].Success.Should().BeFalse();
        results[1].Document.Should().BeNull();
        results[1].ErrorMessage.Should().Contain("HTTP 404");
        results[2].Success.Should().BeTrue();
        results[2].Document!.ExtractedTitle.Should().Be("Page third",
            "the third URL's document must not be reported against the failed second URL");
    }

    [Fact]
    public async Task ImportFromUrlAsync_saves_the_document_and_its_collection_link_together()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var collection = new CollectionEntity { Name = "Reading", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var document = await service.ImportFromUrlAsync("https://example.com/article", collection.Id);

        using var verify = factory.CreateContext();
        verify.DocumentCollections.Should().ContainSingle(link =>
            link.DocumentId == document.Id && link.CollectionId == collection.Id);
        verify.Collections.Single(c => c.Id == collection.Id).DocumentCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportFromUrlAsync_fails_up_front_when_the_collection_does_not_exist()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);

        var act = () => service.ImportFromUrlAsync("https://example.com/article", collectionId: 999);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Collection 999 was not found*");
        db.Documents.Should().BeEmpty("nothing is imported when the requested collection is missing");
        _scraper.Verify(s => s.ExtractContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportFromUrlsAsync_reports_a_missing_collection_on_each_url_instead_of_claiming_success()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);

        var results = await service.ImportFromUrlsAsync(["https://example.com/a"], collectionId: 42);

        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].ErrorMessage.Should().Contain("Collection 42 was not found");
    }

    [Fact]
    public async Task ImportFromUrlAsync_leaves_no_tracked_rows_or_file_behind_when_the_save_fails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var interceptor = new FailingSaveInterceptor();
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new AgentXDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var collection = new CollectionEntity { Name = "Reading", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Collections.Add(collection);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        interceptor.Fail = true;
        var act = () => service.ImportFromUrlAsync("https://example.com/article", collection.Id);
        await act.Should().ThrowAsync<DbUpdateException>();

        db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).Should().BeEmpty(
            "a failed import must not leave pending changes that a later save would retry");
        collection.DocumentCount.Should().Be(0);
        var importDir = Path.Combine(_storageDir, "WebImports");
        (Directory.Exists(importDir) ? Directory.GetFiles(importDir) : []).Should().BeEmpty();

        interceptor.Fail = false;
        db.Tags.Add(new TagEntity { Name = "later", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Documents.Should().BeEmpty();
    }

    // Through the document service
    // Web import wrote its documents and collection counts itself, so imported pages waited
    // for the indexer's idle sweep (no DocumentPendingIndexing) and duplicates were checked
    // by a second copy of the vault's rule.

    [Fact]
    public async Task ImportFromUrlAsync_hands_the_page_to_the_indexer_at_once()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var documentService = CreateDocumentService(db);
        var pending = new List<DocumentPendingIndexingEventArgs>();
        documentService.DocumentPendingIndexing += (_, e) => pending.Add(e);
        var service = CreateService(db, documentService);

        var document = await service.ImportFromUrlAsync("https://example.com/article");

        pending.Should().ContainSingle().Which.DocumentId.Should().Be(document.Id);
        document.IndexingStatus.Should().Be("pending");
        document.FileType.Should().Be("web");
    }

    [Fact]
    public async Task ImportFromUrlAsync_rejects_a_page_already_in_the_vault_with_the_vaults_duplicate_error()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);
        var first = await service.ImportFromUrlAsync("https://example.com/article");

        var act = () => service.ImportFromUrlAsync("https://example.com/article");

        (await act.Should().ThrowAsync<DuplicateDocumentException>())
            .Which.ExistingDocumentId.Should().Be(first.Id);
        Directory.GetFiles(Path.Combine(_storageDir, "WebImports")).Should().ContainSingle(
            "no file is written for a page that is already in the vault");
        using var verify = factory.CreateContext();
        verify.Documents.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportDiscoveredUrlsAsync_skips_local_addresses_listed_by_a_public_feed()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);

        var results = await service.ImportDiscoveredUrlsAsync(
            "https://93.184.216.34/feed.xml",
            ["http://127.0.0.1:9000/admin", "http://169.254.169.254/latest/meta-data", "https://93.184.216.35/post"]);

        results.Select(r => r.Success).Should().Equal(false, false, true);
        results[0].ErrorMessage.Should().StartWith("Skipped");
        _scraper.Verify(s => s.ExtractContentAsync(It.Is<string>(u => u.Contains("127.0.0.1")), It.IsAny<CancellationToken>()), Times.Never);
        _scraper.Verify(s => s.ExtractContentAsync(It.Is<string>(u => u.Contains("169.254.169.254")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportDiscoveredUrlsAsync_imports_private_links_of_an_intranet_sitemap()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var service = CreateService(db);

        var results = await service.ImportDiscoveredUrlsAsync(
            "http://192.168.1.2/sitemap.xml", ["http://192.168.1.3/wiki/page"]);

        results.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new DbUpdateException("Simulated save failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
