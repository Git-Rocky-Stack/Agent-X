using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class SummaryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IHierarchicalSummaryService> _hierarchicalSummaryService = new();
    private readonly ILogger _logger = Log.ForContext<SummaryServiceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task SummarizeDocumentAsync_uses_layered_summary_result_and_preserves_chunk_order()
    {
        using var db = _dbFactory.CreateContext();
        var document = await SeedDocumentAsync(db);

        IReadOnlyList<string>? capturedSections = null;
        string? capturedTitle = null;

        _hierarchicalSummaryService
            .Setup(service => service.BuildSummaryAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((title, sections, _) =>
            {
                capturedTitle = title;
                capturedSections = sections.ToList();
            })
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                DocumentTitle = document.FileName,
                DocumentSummary = "Layered summary output",
                KeyPoints = ["First point", "Second point"],
                TotalSections = 3,
                SectionsIncluded = 3
            });

        var sut = new SummaryService(
            _aiService.Object,
            db,
            _logger,
            _hierarchicalSummaryService.Object);

        var summary = await sut.SummarizeDocumentAsync(document.Id);

        summary.Should().Be("Layered summary output");
        capturedTitle.Should().Be(document.FileName);
        capturedSections.Should().Equal("First chunk", "Second chunk", "Third chunk");
    }

    [Fact]
    public async Task ExtractKeyPointsAsync_returns_key_points_from_layered_summary_result()
    {
        using var db = _dbFactory.CreateContext();
        var document = await SeedDocumentAsync(db);

        _hierarchicalSummaryService
            .Setup(service => service.BuildSummaryAsync(
                document.FileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                DocumentTitle = document.FileName,
                DocumentSummary = "Layered summary output",
                KeyPoints = ["Alpha insight", "Beta finding"],
                TotalSections = 3,
                SectionsIncluded = 3
            });

        var sut = new SummaryService(
            _aiService.Object,
            db,
            _logger,
            _hierarchicalSummaryService.Object);

        var keyPoints = await sut.ExtractKeyPointsAsync(document.Id);

        keyPoints.Should().Equal("Alpha insight", "Beta finding");
    }

    // --- Translation ---
    // Text past 4,000 characters was cut off before it reached the model, and only a log line
    // said so. Longer text is now translated in parts that end at natural breaks.

    [Fact]
    public async Task TranslateTextAsync_sends_text_that_fits_in_one_request_as_it_is()
    {
        using var db = _dbFactory.CreateContext();
        var prompts = CaptureTranslationPrompts();
        var sut = new SummaryService(_aiService.Object, db, _logger, _hierarchicalSummaryService.Object);

        var translation = await sut.TranslateTextAsync("Hello there.", "Spanish");

        prompts.Should().ContainSingle().Which.Should().EndWith("TEXT:\nHello there.");
        translation.Should().Be("[Hello there.]");
    }

    [Fact]
    public async Task TranslateTextAsync_translates_every_paragraph_of_a_long_text_in_order()
    {
        using var db = _dbFactory.CreateContext();
        var prompts = CaptureTranslationPrompts();
        var sut = new SummaryService(_aiService.Object, db, _logger, _hierarchicalSummaryService.Object);
        var first = Paragraph('a', 2500);
        var second = Paragraph('b', 2500);
        var third = Paragraph('c', 2500);

        var translation = await sut.TranslateTextAsync($"{first}\n\n{second}\n\n{third}", "French");

        prompts.Should().HaveCount(3);
        prompts.Select(TextOf).Should().Equal(first, second, third);
        translation.Should().Be($"[{first}]\n\n[{second}]\n\n[{third}]");
    }

    [Fact]
    public async Task TranslateTextAsync_packs_short_paragraphs_into_as_few_requests_as_fit()
    {
        using var db = _dbFactory.CreateContext();
        var prompts = CaptureTranslationPrompts();
        var sut = new SummaryService(_aiService.Object, db, _logger, _hierarchicalSummaryService.Object);
        var paragraphs = Enumerable.Range(0, 10).Select(i => Paragraph((char)('a' + i), 900)).ToList();

        var translation = await sut.TranslateTextAsync(string.Join("\n\n", paragraphs), "German");

        prompts.Should().HaveCount(3, "four 900-character paragraphs and their breaks fit in 4,000 characters");
        string.Concat(prompts.Select(TextOf)).Replace("\n\n", string.Empty)
            .Should().Be(string.Concat(paragraphs), "every paragraph is sent, once, in order");
        translation.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task TranslateTextAsync_splits_one_long_paragraph_at_sentence_ends()
    {
        using var db = _dbFactory.CreateContext();
        var prompts = CaptureTranslationPrompts();
        var sut = new SummaryService(_aiService.Object, db, _logger, _hierarchicalSummaryService.Object);
        var sentences = Enumerable.Range(0, 60).Select(i => $"Sentence {i:D2} {new string('x', 100)}.").ToList();
        var text = string.Join(" ", sentences);

        await sut.TranslateTextAsync(text, "Japanese");

        prompts.Should().HaveCount(2);
        prompts.Select(TextOf).Should().AllSatisfy(part =>
        {
            part.Length.Should().BeLessThanOrEqualTo(SummaryService.MaxTranslationChars);
            part.Should().StartWith("Sentence ").And.EndWith(".");
        });
        string.Join(" ", prompts.Select(TextOf)).Should().Be(text);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(4000)]
    [InlineData(4001)]
    [InlineData(12_345)]
    public void SplitForTranslation_keeps_every_character_and_the_limit(int length)
    {
        var random = new Random(length);
        const string alphabet = "abc de.\n。f!? \n\ngh";
        var text = new string(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

        var parts = SummaryService.SplitForTranslation(text, SummaryService.MaxTranslationChars);

        string.Concat(parts.Select(part => part.Text + part.Separator)).Should().Be(text);
        parts.Should().OnlyContain(part => part.Text.Length <= SummaryService.MaxTranslationChars);
        parts.Should().OnlyContain(part => string.IsNullOrWhiteSpace(part.Separator));
    }

    [Fact]
    public void SplitForTranslation_cuts_a_run_without_any_break_at_the_limit()
    {
        var text = new string('z', 9000);

        var parts = SummaryService.SplitForTranslation(text, 4000);

        parts.Select(part => part.Text.Length).Should().Equal(4000, 4000, 1000);
        string.Concat(parts.Select(part => part.Text)).Should().Be(text);
    }

    [Fact]
    public void SplitForTranslation_splits_cjk_text_after_full_stops()
    {
        var sentence = new string('字', 99) + "。";
        var text = string.Concat(Enumerable.Repeat(sentence, 50));

        var parts = SummaryService.SplitForTranslation(text, 4000);

        parts.Should().HaveCount(2);
        parts.Should().OnlyContain(part => part.Text.EndsWith("。"));
    }

    /// <summary>
    /// Answers every translation request with its text in brackets, and records each prompt.
    /// </summary>
    private List<string> CaptureTranslationPrompts()
    {
        var prompts = new List<string>();
        _aiService
            .Setup(service => service.StreamChatAsync(
                It.IsAny<IReadOnlyList<AgentX.Core.AI.Models.ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<AgentX.Core.AI.Models.ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<AgentX.Core.AI.Models.ChatMessage> messages, string? _, AgentX.Core.AI.Models.ChatOptions? _, CancellationToken _) =>
            {
                var prompt = messages.Single().Content;
                prompts.Add(prompt);
                return Tokens($"[{TextOf(prompt)}]");
            });
        return prompts;
    }

    private static string TextOf(string prompt) => prompt[(prompt.IndexOf("TEXT:\n", StringComparison.Ordinal) + "TEXT:\n".Length)..];

    private static string Paragraph(char letter, int length) => new(letter, length);

    private static async IAsyncEnumerable<string> Tokens(string text)
    {
        await Task.Yield();
        yield return text;
    }

    private static async Task<DocumentEntity> SeedDocumentAsync(AgentX.Core.Data.AgentXDbContext db)
    {
        var document = new DocumentEntity
        {
            FileName = "architecture.md",
            FilePath = @"C:\docs\architecture.md",
            FileType = "md",
            ContentHash = "hash-architecture",
            FileSizeBytes = 2048,
            ImportedAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            FileModifiedAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "completed"
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        db.DocumentChunks.AddRange(
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 2,
                Content = "Third chunk",
                StartCharOffset = 20,
                EndCharOffset = 30,
                TokenCount = 3
            },
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 0,
                Content = "First chunk",
                StartCharOffset = 0,
                EndCharOffset = 10,
                TokenCount = 3
            },
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 1,
                Content = "Second chunk",
                StartCharOffset = 10,
                EndCharOffset = 20,
                TokenCount = 3
            });

        await db.SaveChangesAsync();
        return document;
    }
}
