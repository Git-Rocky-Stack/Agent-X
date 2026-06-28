using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.FeatureFlags;
using AgentX.Core.Services.Tagging;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Tagging;

/// <summary>
/// Coverage for <see cref="AutoTagService"/> — AI-powered tag generation plus manual
/// tag CRUD over an in-memory SQLite <see cref="AgentXDbContext"/>. The AI service and
/// feature-flag service are mocked; a real silent Serilog logger is supplied because the
/// constructor consumes <c>logger.ForContext&lt;T&gt;()</c>. Real temp files exercise the
/// file-read content fallback in <c>GetDocumentContentAsync</c>.
/// </summary>
public sealed class AutoTagServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();
    private readonly Mock<IAiService> _ai = new(MockBehavior.Loose);
    private readonly Mock<IFeatureFlagService> _flags = new(MockBehavior.Loose);
    private readonly List<string> _tempFiles = new();
    private static readonly DateTime FixedDate = new(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc);

    public AutoTagServiceTests()
    {
        _db = _factory.CreateContext();
        _flags.Setup(f => f.IsEnabled(It.IsAny<string>())).Returns(true);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        _logger.Dispose();
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    private AutoTagService CreateSut(bool withFlags = true)
        => new(_db, _ai.Object, _logger, withFlags ? _flags.Object : null);

    private void SetupAiTags(params string[] tags)
        => _ai
            .Setup(a => a.GenerateTagsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

    private void SetupAiChat(string response)
        => _ai
            .Setup(a => a.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private string NewTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private async Task<DocumentEntity> SeedDocumentAsync(
        string fileType = "txt",
        string filePath = "",
        string? summary = null,
        string? title = null,
        string[]? chunks = null)
    {
        var doc = new DocumentEntity
        {
            FileName = "doc." + fileType,
            FilePath = filePath,
            FileType = fileType,
            ContentHash = "deadbeef",
            ImportedAt = FixedDate,
            FileModifiedAt = FixedDate,
            IndexingStatus = "completed",
            Summary = summary,
            ExtractedTitle = title,
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        if (chunks is not null)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                _db.DocumentChunks.Add(new DocumentChunkEntity
                {
                    DocumentId = doc.Id,
                    ChunkIndex = i,
                    Content = chunks[i],
                });
            }
            await _db.SaveChangesAsync();
        }

        return doc;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GenerateTagsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateTags_returns_empty_when_feature_flag_disabled()
    {
        _flags.Setup(f => f.IsEnabled(FeatureFlags.AutoTagging.Name)).Returns(false);

        var tags = await CreateSut().GenerateTagsAsync("some rich document content");

        tags.Should().BeEmpty();
        _ai.Verify(a => a.GenerateTagsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateTags_returns_empty_for_blank_content()
    {
        var tags = await CreateSut().GenerateTagsAsync("   ");
        tags.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateTags_uses_dedicated_ai_method_and_normalizes_and_filters()
    {
        // Take(maxTags) is applied BEFORE the empty-after-normalize filter: the first three
        // entries ["Machine Learning", "###", "Deep-Learning"] are taken, then "###" normalises
        // to empty and is dropped — leaving two normalised tags.
        SetupAiTags("Machine Learning", "###", "Deep-Learning", "NLP", "Vision");

        var tags = await CreateSut().GenerateTagsAsync("content", maxTags: 3);

        tags.Should().HaveCount(2);
        tags.Select(t => t.TagName).Should().ContainInOrder("machine-learning", "deep-learning");
        tags.Should().OnlyContain(t => t.Confidence == 0.85);
    }

    [Fact]
    public async Task GenerateTags_works_without_a_feature_flag_service()
    {
        SetupAiTags("alpha");

        var tags = await CreateSut(withFlags: false).GenerateTagsAsync("content");

        tags.Should().ContainSingle().Which.TagName.Should().Be("alpha");
    }

    [Fact]
    public async Task GenerateTags_falls_back_to_chat_json_when_dedicated_method_empty()
    {
        SetupAiTags(); // empty → trigger ChatAsync fallback
        SetupAiChat("""Sure! [{"tag":"Alpha","confidence":0.95},{"name":"beta"},{"tag":"!!!"}] done""");

        var tags = await CreateSut().GenerateTagsAsync("content", maxTags: 5);

        // "tag" + "name" property branches; "!!!" normalises to empty and is dropped.
        tags.Select(t => t.TagName).Should().ContainInOrder("alpha", "beta");
        tags.Should().HaveCount(2);
        tags[0].Confidence.Should().BeApproximately(0.95, 1e-9);
        tags[1].Confidence.Should().BeApproximately(0.7, 1e-9); // FallbackConfidence default
    }

    [Fact]
    public async Task GenerateTags_clamps_out_of_range_confidence_from_json()
    {
        SetupAiTags();
        SetupAiChat("""[{"tag":"x","confidence":5.0}]""");

        var tags = await CreateSut().GenerateTagsAsync("content");

        tags.Should().ContainSingle();
        tags[0].Confidence.Should().Be(1.0);
    }

    [Fact]
    public async Task GenerateTags_falls_back_to_delimiter_split_when_no_json_array()
    {
        SetupAiTags();
        SetupAiChat("1. Alpha, beta; gamma\n- delta");

        var tags = await CreateSut().GenerateTagsAsync("content", maxTags: 10);

        tags.Select(t => t.TagName).Should().Contain(new[] { "alpha", "beta", "gamma", "delta" });
        tags.Should().OnlyContain(t => t.Confidence == 0.7);
    }

    [Fact]
    public async Task GenerateTags_returns_empty_when_chat_fallback_is_empty()
    {
        SetupAiTags();
        SetupAiChat("");

        var tags = await CreateSut().GenerateTagsAsync("content");

        tags.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateTags_recovers_when_dedicated_method_throws()
    {
        _ai
            .Setup(a => a.GenerateTagsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model error"));
        SetupAiChat("""[{"tag":"recovered"}]""");

        var tags = await CreateSut().GenerateTagsAsync("content");

        tags.Should().ContainSingle().Which.TagName.Should().Be("recovered");
    }

    [Fact]
    public async Task GenerateTags_returns_empty_when_chat_fallback_throws()
    {
        SetupAiTags();
        _ai
            .Setup(a => a.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("chat down"));

        var tags = await CreateSut().GenerateTagsAsync("content");

        tags.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ApplyAutoTagsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAutoTags_returns_early_when_feature_flag_disabled()
    {
        _flags.Setup(f => f.IsEnabled(FeatureFlags.AutoTagging.Name)).Returns(false);
        var doc = await SeedDocumentAsync(chunks: new[] { "lots of content here" });

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAutoTags_throws_when_document_missing()
    {
        var act = () => CreateSut().ApplyAutoTagsAsync(99999);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ApplyAutoTags_returns_when_no_extractable_content()
    {
        // No chunks, no file, no summary, no title → empty content → no tags applied.
        var doc = await SeedDocumentAsync(fileType: "pdf");

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAutoTags_returns_when_no_tags_generated()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "some content" });
        SetupAiTags();        // dedicated method empty
        SetupAiChat("");      // chat fallback empty

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAutoTags_creates_tags_and_associations_from_chunks()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "first chunk", "second chunk" });
        SetupAiTags("alpha", "beta");

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        await using var verify = _factory.CreateContext();
        (await verify.Tags.CountAsync()).Should().Be(2);
        (await verify.Tags.AllAsync(t => t.IsAutoGenerated)).Should().BeTrue();
        (await verify.DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id)).Should().Be(2);
    }

    [Fact]
    public async Task ApplyAutoTags_reuses_existing_tag_and_skips_existing_association()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var existingTag = new TagEntity { Name = "alpha", CreatedAt = FixedDate };
        _db.Tags.Add(existingTag);
        await _db.SaveChangesAsync();
        _db.DocumentTags.Add(new DocumentTagEntity
        {
            DocumentId = doc.Id,
            TagId = existingTag.Id,
            Confidence = 0.5,
            AssignedAt = FixedDate,
        });
        await _db.SaveChangesAsync();

        SetupAiTags("alpha", "beta"); // alpha exists+assigned (skip), beta is new

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        await using var verify = _factory.CreateContext();
        (await verify.Tags.CountAsync()).Should().Be(2); // alpha reused, beta created
        (await verify.DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id)).Should().Be(2);
        // The original alpha association keeps its manual confidence (not overwritten).
        var alpha = await verify.Tags.FirstAsync(t => t.Name == "alpha");
        (await verify.DocumentTags.FirstAsync(dt => dt.TagId == alpha.Id)).Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ApplyAutoTags_reads_content_from_text_file_when_no_chunks()
    {
        var path = NewTempFile("This document body comes straight from disk.");
        var doc = await SeedDocumentAsync(fileType: "txt", filePath: path);
        SetupAiTags("fromfile");

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAutoTags_uses_summary_when_no_chunks_and_non_text_file()
    {
        var doc = await SeedDocumentAsync(fileType: "pdf", summary: "An AI generated summary of the document.");
        SetupAiTags("fromsummary");

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAutoTags_uses_extracted_title_as_last_resort()
    {
        var doc = await SeedDocumentAsync(fileType: "pdf", title: "Quarterly Financial Report");
        SetupAiTags("fromtitle");

        await CreateSut().ApplyAutoTagsAsync(doc.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAutoTags_propagates_cancellation()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var act = () => CreateSut().ApplyAutoTagsAsync(doc.Id, new CancellationToken(canceled: true));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Tag CRUD
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTags_returns_tags_ordered_by_name()
    {
        _db.Tags.AddRange(
            new TagEntity { Name = "zebra", CreatedAt = FixedDate },
            new TagEntity { Name = "alpha", CreatedAt = FixedDate },
            new TagEntity { Name = "mango", CreatedAt = FixedDate });
        await _db.SaveChangesAsync();

        var tags = await CreateSut().GetAllTagsAsync();

        tags.Select(t => t.Name).Should().ContainInOrder("alpha", "mango", "zebra");
    }

    [Fact]
    public async Task CreateTag_creates_normalized_tag_with_color()
    {
        var tag = await CreateSut().CreateTagAsync("Machine Learning", "  #FF5733  ");

        tag.Name.Should().Be("machine-learning");
        tag.ColorHex.Should().Be("#FF5733");
        tag.IsAutoGenerated.Should().BeFalse();
        (await _factory.CreateContext().Tags.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateTag_rejects_blank_name(string name)
    {
        var act = () => CreateSut().CreateTagAsync(name);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateTag_rejects_case_insensitive_duplicate()
    {
        await CreateSut().CreateTagAsync("Alpha");

        var act = () => CreateSut().CreateTagAsync("alpha");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteTag_removes_tag_and_cascades_associations()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var tag = new TagEntity { Name = "doomed", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        _db.DocumentTags.Add(new DocumentTagEntity
        {
            DocumentId = doc.Id,
            TagId = tag.Id,
            Confidence = 1.0,
            AssignedAt = FixedDate,
        });
        await _db.SaveChangesAsync();

        await CreateSut().DeleteTagAsync(tag.Id);

        await using var verify = _factory.CreateContext();
        (await verify.Tags.CountAsync()).Should().Be(0);
        (await verify.DocumentTags.CountAsync()).Should().Be(0); // cascade
    }

    [Fact]
    public async Task DeleteTag_unknown_id_is_no_op()
    {
        var act = () => CreateSut().DeleteTagAsync(99999);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Manual assignment
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignTag_creates_association_with_full_confidence()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var tag = new TagEntity { Name = "manual", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        await CreateSut().AssignTagAsync(doc.Id, tag.Id);

        var association = await _factory.CreateContext().DocumentTags
            .FirstAsync(dt => dt.DocumentId == doc.Id && dt.TagId == tag.Id);
        association.Confidence.Should().Be(1.0);
    }

    [Fact]
    public async Task AssignTag_throws_when_document_missing()
    {
        var tag = new TagEntity { Name = "t", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var act = () => CreateSut().AssignTagAsync(99999, tag.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AssignTag_throws_when_tag_missing()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });

        var act = () => CreateSut().AssignTagAsync(doc.Id, 99999);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AssignTag_is_idempotent_for_duplicate_assignment()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var tag = new TagEntity { Name = "dup", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        var sut = CreateSut();

        await sut.AssignTagAsync(doc.Id, tag.Id);
        await sut.AssignTagAsync(doc.Id, tag.Id); // second call no-ops

        (await _factory.CreateContext().DocumentTags.CountAsync(dt => dt.DocumentId == doc.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task RemoveTag_deletes_existing_association()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var tag = new TagEntity { Name = "removeme", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        _db.DocumentTags.Add(new DocumentTagEntity
        {
            DocumentId = doc.Id,
            TagId = tag.Id,
            Confidence = 1.0,
            AssignedAt = FixedDate,
        });
        await _db.SaveChangesAsync();

        await CreateSut().RemoveTagAsync(doc.Id, tag.Id);

        (await _factory.CreateContext().DocumentTags.CountAsync()).Should().Be(0);
        // The tag itself survives — only the association is removed.
        (await _factory.CreateContext().Tags.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveTag_missing_association_is_no_op()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var act = () => CreateSut().RemoveTagAsync(doc.Id, 99999);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Tag lookups
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagsForDocument_returns_assigned_tags_ordered_by_name()
    {
        var doc = await SeedDocumentAsync(chunks: new[] { "content" });
        var zebra = new TagEntity { Name = "zebra", CreatedAt = FixedDate };
        var alpha = new TagEntity { Name = "alpha", CreatedAt = FixedDate };
        _db.Tags.AddRange(zebra, alpha);
        await _db.SaveChangesAsync();
        _db.DocumentTags.AddRange(
            new DocumentTagEntity { DocumentId = doc.Id, TagId = zebra.Id, Confidence = 1.0, AssignedAt = FixedDate },
            new DocumentTagEntity { DocumentId = doc.Id, TagId = alpha.Id, Confidence = 1.0, AssignedAt = FixedDate });
        await _db.SaveChangesAsync();

        var tags = await CreateSut().GetTagsForDocumentAsync(doc.Id);

        tags.Select(t => t.Name).Should().ContainInOrder("alpha", "zebra");
    }

    [Fact]
    public async Task GetTagsForDocuments_empty_input_returns_empty_dictionary()
    {
        var result = await CreateSut().GetTagsForDocumentsAsync(Array.Empty<long>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTagsForDocuments_returns_tags_keyed_by_document_with_empty_arrays()
    {
        var doc1 = await SeedDocumentAsync(chunks: new[] { "content" });
        var doc2 = await SeedDocumentAsync(chunks: new[] { "content" });
        var tag = new TagEntity { Name = "shared", CreatedAt = FixedDate };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        _db.DocumentTags.Add(new DocumentTagEntity
        {
            DocumentId = doc1.Id,
            TagId = tag.Id,
            Confidence = 1.0,
            AssignedAt = FixedDate,
        });
        await _db.SaveChangesAsync();

        var result = await CreateSut().GetTagsForDocumentsAsync(new[] { doc1.Id, doc2.Id });

        result.Should().ContainKeys(doc1.Id, doc2.Id);
        result[doc1.Id].Should().ContainSingle().Which.Name.Should().Be("shared");
        result[doc2.Id].Should().BeEmpty();
    }
}
