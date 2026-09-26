using System.Globalization;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Annotations;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Annotations;

/// <summary>
/// CU27: the Markdown export writes ISO-style timestamps, which must not pick up the current
/// culture's calendar (th-TH uses the Buddhist era, ar-SA the Hijri calendar).
/// </summary>
public sealed class AnnotationExportCultureTests
{
    [Fact]
    public async Task ExportAnnotationsAsMarkdownAsync_writes_gregorian_timestamps_under_a_thai_culture()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var document = new DocumentEntity { FileName = "guide.pdf", ImportedAt = DateTime.UtcNow };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var created = new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc);
        db.Annotations.Add(new AnnotationEntity
        {
            DocumentId = document.Id,
            HighlightedText = "Latency budgets matter",
            CreatedAt = created,
            UpdatedAt = created.AddHours(2),
        });
        await db.SaveChangesAsync();
        using var logger = new LoggerConfiguration().CreateLogger();
        var sut = new AnnotationService(db, logger);

        var original = CultureInfo.CurrentCulture;
        string markdown;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            markdown = await sut.ExportAnnotationsAsMarkdownAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        markdown.Should().Contain("_Created: 2026-09-26 08:30 UTC");
        markdown.Should().Contain("Updated: 2026-09-26 10:30 UTC");
        markdown.Should().Contain($"on {DateTime.UtcNow.Year}-");
        markdown.Should().NotContain("2569-", "2569 is the Buddhist-era year for 2026");
    }
}
