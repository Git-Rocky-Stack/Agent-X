using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
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
/// A new highlight reaches Temporal Identity. ProcessAnnotationAsync had no caller, so
/// highlights never became insight moments for Past Self.
/// </summary>
public sealed class AnnotationTemporalIdentityTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task CreateAnnotationAsync_keeps_the_highlight_as_an_insight_moment()
    {
        using var db = _factory.CreateContext();
        var documentId = await SeedDocumentAsync(db);
        var sut = new AnnotationService(db, _logger, new TemporalIdentityService(db));

        var annotation = await sut.CreateAnnotationAsync(
            documentId, chunkId: null, startOffset: 0, endOffset: 22,
            "Latency budgets matter", "yellow", noteText: "Budgets beat heroics");

        var insight = await db.Set<InsightMomentEntity>().AsNoTracking().SingleAsync();
        insight.SourceType.Should().Be(InsightSource.DocumentAnnotation);
        insight.SourceId.Should().Be(annotation.Id);
        insight.InsightText.Should().Be("Budgets beat heroics");
    }

    [Fact]
    public async Task CreateAnnotationAsync_when_temporal_identity_fails_still_returns_the_saved_annotation()
    {
        using var db = _factory.CreateContext();
        var documentId = await SeedDocumentAsync(db);
        var temporalIdentity = new Mock<ITemporalIdentityService>();
        temporalIdentity
            .Setup(service => service.ProcessAnnotationAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is busy"));
        var sut = new AnnotationService(db, _logger, temporalIdentity.Object);

        var annotation = await sut.CreateAnnotationAsync(
            documentId, chunkId: null, startOffset: 0, endOffset: 22, "Latency budgets matter", "green");

        annotation.Id.Should().BeGreaterThan(0);
        (await db.Annotations.AsNoTracking().CountAsync()).Should().Be(1);
        temporalIdentity.Verify(
            service => service.ProcessAnnotationAsync(annotation.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<long> SeedDocumentAsync(AgentX.Core.Data.AgentXDbContext db)
    {
        var document = new DocumentEntity { FileName = "guide.pdf", ImportedAt = DateTime.UtcNow };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    public void Dispose()
    {
        _logger.Dispose();
        _factory.Dispose();
    }
}
