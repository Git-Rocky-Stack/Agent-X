using AgentX.App.ViewModels;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Annotations;

/// <summary>
/// The path of an annotation made in the Knowledge Vault preview, against a real database: the
/// passages are the document's chunks, a highlight is saved with its chunk and offsets, it
/// reaches Temporal Identity and the Annotations page, and it goes with its document.
/// </summary>
public sealed class VaultAnnotationPathTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task GetPassageAsync_ServesTheChunksInReadingOrder_AndClampsThePosition()
    {
        using var db = _factory.CreateContext();
        var (documentId, chunkIds) = await SeedDocumentAsync(db, "First passage.", "Second passage.", "Third passage.");
        var sut = new AnnotationService(db, _logger);

        var first = await sut.GetPassageAsync(documentId, 0);
        var second = await sut.GetPassageAsync(documentId, 1);
        var last = await sut.GetPassageAsync(documentId, 2);

        first.Should().Be(new AnnotationPassage(chunkIds[0], 0, 3, 1, "First passage."));
        second.Should().Be(new AnnotationPassage(chunkIds[1], 1, 3, 1, "Second passage."));
        last.Should().Be(new AnnotationPassage(chunkIds[2], 2, 3, 1, "Third passage."));
        (await sut.GetPassageAsync(documentId, 99)).Should().Be(last);
        (await sut.GetPassageAsync(documentId, -3)).Should().Be(first);
    }

    [Fact]
    public async Task GetPassageAsync_ForADocumentWithoutIndexedText_ReturnsNull()
    {
        using var db = _factory.CreateContext();
        var (documentId, _) = await SeedDocumentAsync(db);
        var sut = new AnnotationService(db, _logger);

        (await sut.GetPassageAsync(documentId, 0)).Should().BeNull();
    }

    [Fact]
    public async Task CreateAnnotationAsync_StoresABlankNoteAsNoNote()
    {
        // A whitespace note was stored as an empty string, which UpdateAnnotationAsync already
        // treats as no note.
        using var db = _factory.CreateContext();
        var (documentId, chunkIds) = await SeedDocumentAsync(db, "Latency budgets matter.");
        var sut = new AnnotationService(db, _logger);

        var annotation = await sut.CreateAnnotationAsync(documentId, chunkIds[0], 0, 7, "Latency", "yellow", "   ");

        annotation.NoteText.Should().BeNull();
        (await db.Annotations.AsNoTracking().SingleAsync()).NoteText.Should().BeNull();
    }

    [Fact]
    public async Task AnAnnotationMadeInTheVaultPreview_ReachesTheAnnotationsPageAndTemporalIdentity()
    {
        using var db = _factory.CreateContext();
        var (documentId, chunkIds) = await SeedDocumentAsync(db, "Latency budgets matter.", "Budgets beat heroics.");
        var service = new AnnotationService(db, _logger, new TemporalIdentityService(db));
        var preview = new DocumentNotesViewModel(service);

        await preview.ShowDocumentAsync(documentId);
        await preview.NextPassageCommand.ExecuteAsync(null);
        preview.CaptureSelection("beat heroics", positionHint: 8);
        preview.DraftNote = "Plan for the slow path";
        await preview.SaveAnnotationCommand.ExecuteAsync(null);

        var stored = await db.Annotations.AsNoTracking().SingleAsync();
        stored.DocumentId.Should().Be(documentId);
        stored.ChunkId.Should().Be(chunkIds[1]);
        stored.StartOffset.Should().Be(8);
        stored.EndOffset.Should().Be(20);
        stored.HighlightedText.Should().Be("beat heroics");
        stored.NoteText.Should().Be("Plan for the slow path");
        stored.Color.Should().Be("yellow");
        preview.Annotations.Should().ContainSingle().Which.Id.Should().Be(stored.Id);

        var annotationsPage = new AnnotationsViewModel(service, EnglishResources.Create());
        await annotationsPage.InitializeAsync();

        annotationsPage.TotalCount.Should().Be(1);
        annotationsPage.Annotations.Should().ContainSingle();
        annotationsPage.Annotations[0].DocumentName.Should().Be("guide.pdf");
        annotationsPage.Annotations[0].HighlightedText.Should().Be("beat heroics");
        annotationsPage.Annotations[0].NoteText.Should().Be("Plan for the slow path");

        var insight = await db.Set<InsightMomentEntity>().AsNoTracking().SingleAsync();
        insight.SourceType.Should().Be(InsightSource.DocumentAnnotation);
        insight.SourceId.Should().Be(stored.Id);
    }

    [Fact]
    public async Task DeletingTheDocument_DeletesItsAnnotations_EvenWithoutForeignKeyEnforcement()
    {
        // The delete confirmation says a document's annotations go with it. The database
        // cascade only runs when the connection enforces foreign keys, so DeleteDocumentAsync
        // sweeps them up like it already did chunks, collection links and tags.
        using var db = _factory.CreateContext();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var (documentId, chunkIds) = await SeedDocumentAsync(db, "Latency budgets matter.");
        var annotations = new AnnotationService(db, _logger);
        await annotations.CreateAnnotationAsync(documentId, chunkIds[0], 0, 7, "Latency", "yellow");
        db.ChangeTracker.Clear();
        var documents = new DocumentService(db, Array.Empty<IDocumentProcessor>(), Mock.Of<ISettingsService>(), _logger);

        await documents.DeleteDocumentAsync(documentId);

        using var fresh = _factory.CreateContext();
        (await fresh.Documents.CountAsync()).Should().Be(0);
        (await fresh.Annotations.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Adds "guide.pdf" with one chunk per text, inserted in reverse so reading order has to
    /// come from the chunk index. Returns the chunk ids in reading order.
    /// </summary>
    private static async Task<(long DocumentId, long[] ChunkIds)> SeedDocumentAsync(AgentXDbContext db, params string[] passages)
    {
        var document = new DocumentEntity { FileName = "guide.pdf", ImportedAt = DateTime.UtcNow };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var chunks = new DocumentChunkEntity[passages.Length];
        for (var index = passages.Length - 1; index >= 0; index--)
        {
            chunks[index] = new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = index,
                Content = passages[index],
                PageNumber = 1,
            };
            db.DocumentChunks.Add(chunks[index]);
            await db.SaveChangesAsync();
        }

        return (document.Id, chunks.Select(chunk => chunk.Id).ToArray());
    }

    public void Dispose()
    {
        _logger.Dispose();
        _factory.Dispose();
    }
}
