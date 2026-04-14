using AgentX.Core.AI.Routing;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI.Routing;

public class TaskTypeDetectorTests
{
    private readonly TaskTypeDetector _detector = new();

    // ── Explicit Tag Override Tests ─────────────────────────────────

    [Theory]
    [InlineData("[extraction] get the names from this", "extraction")]
    [InlineData("[summarization] condense this article", "summarization")]
    [InlineData("[analysis] compare these two approaches", "analysis")]
    [InlineData("[generation] write an essay about AI", "generation")]
    [InlineData("[code] implement a binary search", "code")]
    [InlineData("[creative] write a poem about the sea", "creative")]
    [InlineData("[chat] hello there", "chat")]
    [InlineData("[embedding] vectorize this text", "embedding")]
    [InlineData("[ANALYSIS] deep dive into the data", "analysis")]
    [InlineData("[Code] fix the bug", "code")]
    public void Detect_WithTagPrefix_ReturnsTaggedType(string prompt, string expectedType)
    {
        var result = _detector.Detect(prompt);
        result.Name.Should().Be(expectedType);
    }

    [Fact]
    public void Detect_WithUnknownTag_ReturnsChat()
    {
        var result = _detector.Detect("[unknown-tag] some text here");
        result.Should().BeSameAs(TaskType.Chat);
    }

    [Fact]
    public void Detect_TagNotAtStart_FallsBackToKeywordMatch()
    {
        // Tags only work as a prefix at the very start of the prompt
        var result = _detector.Detect("Please [analysis] do something");
        // The tag at mid-string is ignored, but "analysis" matches via keyword
        result.Should().BeSameAs(TaskType.Analysis);
    }

    // ── Keyword Matching Tests ──────────────────────────────────────

    [Theory]
    [InlineData("Extract the key entities from this document", "extraction")]
    [InlineData("Parse data from the API response", "extraction")]
    [InlineData("Pull data from the spreadsheet", "extraction")]
    [InlineData("Entity recognition on this text", "extraction")]
    public void Detect_ExtractionKeywords_ReturnsExtraction(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Summarize this article for me", "summarization")]
    [InlineData("Give me a summary of the meeting", "summarization")]
    [InlineData("TLDR of this long text", "summarization")]
    [InlineData("Condense this into a brief overview", "summarization")]
    public void Detect_SummarizationKeywords_ReturnsSummarization(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Analyze the market trends", "analysis")]
    [InlineData("Compare these two datasets", "analysis")]
    [InlineData("Evaluate the performance metrics", "analysis")]
    [InlineData("Critique my argument", "analysis")]
    [InlineData("Review the pull request", "analysis")]
    public void Detect_AnalysisKeywords_ReturnsAnalysis(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Write a blog post about AI", "generation")]
    [InlineData("Generate a marketing plan", "generation")]
    [InlineData("Draft an email to the team", "generation")]
    [InlineData("Compose a formal letter", "generation")]
    public void Detect_GenerationKeywords_ReturnsGeneration(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Code a sorting algorithm", "code")]
    [InlineData("Debug this function", "code")]
    [InlineData("Implement the new feature", "code")]
    [InlineData("Refactor the legacy class", "code")]
    [InlineData("Write code to sort this", "code")]
    public void Detect_CodeKeywords_ReturnsCode(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Write a creative story", "creative")]
    [InlineData("Brainstorm ideas for the campaign", "creative")]
    [InlineData("Imagine a futuristic city", "creative")]
    public void Detect_CreativeKeywords_ReturnsCreative(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("Embed this document", "embedding")]
    [InlineData("Generate an embedding for this text", "embedding")]
    [InlineData("Vectorize the input", "embedding")]
    public void Detect_EmbeddingKeywords_ReturnsEmbedding(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    // ── Default Fallback Tests ──────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Hello, how are you?")]
    [InlineData("What is the weather today?")]
    [InlineData("Tell me a joke")]
    public void Detect_EmptyOrUnrecognized_ReturnsChat(string? prompt)
    {
        var result = _detector.Detect(prompt!);
        result.Should().BeSameAs(TaskType.Chat);
    }

    // ── Case Insensitivity ─────────────────────────────────────────

    [Theory]
    [InlineData("ANALYZE the data", "analysis")]
    [InlineData("Summarize THIS", "summarization")]
    [InlineData("CODE the solution", "code")]
    [InlineData("EXTRACT the names", "extraction")]
    public void Detect_KeywordsAreCaseInsensitive(string prompt, string expected)
    {
        _detector.Detect(prompt).Name.Should().Be(expected);
    }

    // ── First Match Wins ───────────────────────────────────────────

    [Fact]
    public void Detect_MultipleKeywords_ReturnsFirstMatch()
    {
        // "extract" comes before "summarize" in the keyword map
        var result = _detector.Detect("Extract and summarize this document");
        result.Name.Should().Be("extraction");
    }
}