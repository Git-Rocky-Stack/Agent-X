using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace AgentX.Tests.Services.Intelligence;

public sealed class KnowledgeGraphServiceTests
{
    private static async Task SeedAsync(AgentX.Core.Data.AgentXDbContext db)
    {
        var now = DateTime.UtcNow;
        var tag = new TagEntity { Name = "finance", CreatedAt = now };
        db.Tags.Add(tag);
        for (var i = 1; i <= 3; i++)
        {
            var doc = new DocumentEntity
            {
                FileName = $"doc{i}.md",
                FilePath = $"/docs/doc{i}.md",
                FileType = "md",
                ContentHash = $"h{i}",
                ImportedAt = now,
                FileModifiedAt = now,
                IndexingStatus = "completed",
            };
            doc.DocumentTags.Add(new DocumentTagEntity { Document = doc, Tag = tag });
            db.Documents.Add(doc);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task BuildGraphAsync_uses_the_design_palette_for_node_reference_colors()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        await SeedAsync(db);

        var graph = await new KnowledgeGraphService(db, Logger.None).BuildGraphAsync();

        graph.Nodes.Where(n => n.NodeType == GraphNodeType.Document).Should().OnlyContain(n => n.ColorHex == "#58C4BC");
        graph.Nodes.Where(n => n.NodeType == GraphNodeType.Tag).Should().OnlyContain(n => n.ColorHex == "#FFB000");
        graph.Edges.Should().NotContain(e => e.ColorHex == "#6366F1", "DESIGN.md bans indigo");
        graph.Edges.Should().Contain(e => e.SourceId.StartsWith("doc-") && e.TargetId.StartsWith("doc-"));
    }

    [Fact]
    public async Task BuildGraphAsync_stops_when_cancelled()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        await SeedAsync(db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new KnowledgeGraphService(db, Logger.None).BuildGraphAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
