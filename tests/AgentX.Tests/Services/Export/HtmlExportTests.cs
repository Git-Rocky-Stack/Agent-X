using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="HtmlExport"/>.
/// Tests the HTML format strategy implementation.
/// </summary>
public sealed class HtmlExportTests
{
    private readonly HtmlExport _export;

    public HtmlExportTests()
    {
        _export = new HtmlExport();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Basic Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldReturnHtml()
    {
        // Act
        var format = _export.Format;

        // Assert
        format.Should().Be(ExportFormat.Html);
    }

    [Fact]
    public void FileExtension_ShouldReturnHtmlExtension()
    {
        // Act
        var extension = _export.FileExtension;

        // Assert
        extension.Should().Be(".html");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Supports<T> Method
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Supports_WithConversationEntity_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<ConversationEntity>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithConversationList_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<IReadOnlyList<ConversationEntity>>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithSearchResults_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<IReadOnlyList<SearchResultExportItem>>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithUnsupportedType_ShouldReturnFalse()
    {
        // Act
        var result = _export.Supports<string>();

        // Assert
        result.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Single Conversation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithSingleConversation_ReturnsValidHtml()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 2,
            TokensUsed = 100,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 10,
                    SortOrder = 0
                },
                new MessageEntity
                {
                    Id = 2,
                    Role = "assistant",
                    Content = "Hi there!",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 15,
                    SortOrder = 1
                }
            }
        };

        var options = new ExportOptions
        {
            IncludeMetadata = true,
            IncludeTimestamps = true,
            IncludeModelInfo = true,
            IncludeCitations = true
        };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("Test Conversation");
        html.Should().Contain("<div class=\"conversation\">");
        html.Should().Contain("</html>");
    }

    [Fact]
    public async Task RenderAsync_ListsEachAnswersWebSourcesInsideIt_Encoded()
    {
        var html = (string)await _export.RenderAsync(
            ResearchConversation.Create(), new ExportOptions { IncludeCitations = true });

        var second = html.IndexOf(ResearchConversation.SecondAnswer, StringComparison.Ordinal);
        html[..second].Should().Contain("<li>Migration &lt;guide&gt; - https://example.org/migrate?a=1&amp;b=2</li>");
        html[second..].Should().Contain("<li>Blog - https://blog.example.org/v2</li>")
            .And.NotContain("Release notes");
        html.Should().NotContain("<h2>Citations</h2>");
    }

    [Fact]
    public async Task RenderAsync_WithSingleConversation_IncludesMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Metadata Test",
            CreatedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 15, 11, 45, 0, DateTimeKind.Utc),
            MessageCount = 5,
            TokensUsed = 1250,
            ModelId = "claude-3-5-sonnet",
            Messages = new List<MessageEntity>()
        };

        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("Created: 2024-01-15 10:30:00 UTC");
        html.Should().Contain("Updated: 2024-01-15 11:45:00 UTC");
        html.Should().Contain("Messages: 5");
        html.Should().Contain("Tokens: 1,250");
        html.Should().Contain("Model: claude-3-5-sonnet");
    }

    [Fact]
    public async Task RenderAsync_WithSingleConversation_ExcludesMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "No Metadata Test",
            CreatedAt = DateTime.UtcNow,
            MessageCount = 3,
            TokensUsed = 500,
            ModelId = "claude-3-5-sonnet",
            Messages = new List<MessageEntity>()
        };

        var options = new ExportOptions { IncludeMetadata = false };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotContain("Created:");
        html.Should().NotContain("Messages: 3");
        html.Should().NotContain("Model: claude-3-5-sonnet");
    }

    [Fact]
    public async Task RenderAsync_WithUserMessage_AppliesUserClass()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "User Message Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello from user",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("class=\"message user\"");
        html.Should().Contain("Hello from user");
    }

    [Fact]
    public async Task RenderAsync_WithAssistantMessage_AppliesAssistantClass()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Assistant Message Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Hello from assistant",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("class=\"message assistant\"");
        html.Should().Contain("Hello from assistant");
    }

    [Fact]
    public async Task RenderAsync_WithSystemMessage_ExcludesFromBody()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "System Message Test",
            CreatedAt = DateTime.UtcNow,
            SystemPrompt = "You are a helpful assistant",
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "system",
                    Content = "System directive",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                },
                new MessageEntity
                {
                    Id = 2,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 1
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("System Prompt");
        html.Should().Contain("You are a helpful assistant");
        html.Should().NotContain("System directive");
        html.Should().NotContain("class=\"message system\"");
    }

    [Fact]
    public async Task RenderAsync_WithHtmlSpecialCharacters_EscapesContent()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "HTML Escape Test <script>",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Test <script>alert('xss')</script>",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Multiple Conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithMultipleConversations_ReturnsValidHtml()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new ConversationEntity
            {
                Id = 1,
                Title = "First Conversation",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>
                {
                    new MessageEntity { Id = 1, Role = "user", Content = "First", Timestamp = DateTime.UtcNow, SortOrder = 0 }
                }
            },
            new ConversationEntity
            {
                Id = 2,
                Title = "Second Conversation",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>
                {
                    new MessageEntity { Id = 1, Role = "user", Content = "Second", Timestamp = DateTime.UtcNow, SortOrder = 0 }
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversations, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("First Conversation");
        html.Should().Contain("Second Conversation");
        html.Should().Contain("class=\"section-divider\"");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Search Results
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithSearchResults_ReturnsValidHtml()
    {
        // Arrange
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Query = "test query",
                DocumentName = "Test Document.pdf",
                Content = "Search result content here",
                RelevanceScore = 0.95f,
                Citations = new List<string> { "Page 1" }
            }
        };

        var options = new ExportOptions { IncludeMetadata = true, IncludeCitations = true };

        // Act
        var result = await _export.RenderAsync(results, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("Search Results");
        html.Should().Contain("Test Document.pdf");
        html.Should().Contain("Relevance: 95.0%");
        html.Should().Contain("Page 1");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cancellation Support
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _export.RenderAsync(conversation, new ExportOptions(), cts.Token));
    }

    // Untrusted content (model output, document excerpts) must never become script

    /// <summary>Payloads that ran script in an exported page before the renderer was locked down.</summary>
    public static IEnumerable<object[]> ScriptPayloads() => new[]
    {
        new object[] { "<img src=x onerror=alert(1)>" },
        new object[] { "<script>alert(1)</script>" },
        new object[] { "[click me](javascript:alert(1))" },
        new object[] { "[click me](JaVaScRiPt:alert(1))" },
        new object[] { "[click me](<java\tscript:alert(1)>)" },
        new object[] { "[click me][ref]\n\n[ref]: javascript:alert(1)" },
        new object[] { "<javascript:alert(1)>" },
        new object[] { "![logo](javascript:alert(1))" },
        new object[] { "[data](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)" },
        new object[] { "# Title {onclick=alert(1)}\n\n[x](https://example.com){onmouseover=alert(1)}" },
    };

    [Theory]
    [MemberData(nameof(ScriptPayloads))]
    public async Task RenderAsync_AssistantMessageWithScriptPayload_RendersNothingExecutable(string payload)
    {
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Injected",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new() { Id = 1, Role = "assistant", Content = payload, Timestamp = DateTime.UtcNow, SortOrder = 0 },
            },
        };

        var html = (string)await _export.RenderAsync(conversation, new ExportOptions());

        AssertNoExecutableMarkup(html);
    }

    [Theory]
    [MemberData(nameof(ScriptPayloads))]
    public async Task RenderAsync_SearchResultWithScriptPayload_RendersNothingExecutable(string payload)
    {
        var results = new List<SearchResultExportItem>
        {
            new() { Query = "q", DocumentName = "doc.md", Content = payload, RelevanceScore = 0.5f },
        };

        var html = (string)await _export.RenderAsync(results, new ExportOptions());

        AssertNoExecutableMarkup(html);
    }

    [Fact]
    public async Task RenderAsync_AssistantMarkdown_KeepsFormattingAndSafeLinks()
    {
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Formatting",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new()
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "**bold** and `code`\n\n[site](https://example.com/a?b=1) [mail](mailto:me@example.com) [local](notes/today.md)\n\n| a | b |\n|---|---|\n| 1 | 2 |",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0,
                },
            },
        };

        var html = (string)await _export.RenderAsync(conversation, new ExportOptions());

        html.Should().Contain("<strong>bold</strong>");
        html.Should().Contain("<code>code</code>");
        html.Should().Contain("href=\"https://example.com/a?b=1\"");
        html.Should().Contain("href=\"mailto:me@example.com\"");
        html.Should().Contain("href=\"notes/today.md\"");
        html.Should().Contain("<table>");
    }

    [Fact]
    public async Task RenderAsync_EveryDocument_DeclaresARestrictiveContentSecurityPolicy()
    {
        var conversation = new ConversationEntity { Id = 1, Title = "CSP", CreatedAt = DateTime.UtcNow };

        var html = (string)await _export.RenderAsync(conversation, new ExportOptions());

        html.Should().Contain("<meta http-equiv=\"Content-Security-Policy\"");
        html.Should().Contain("default-src 'none'");
        html.Should().NotContain("script-src");
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("HTTP://example.com", true)]
    [InlineData("mailto:a@b.c", true)]
    [InlineData("docs/readme.md", true)]
    [InlineData("#section", true)]
    [InlineData("/path/with:colon", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(" javascript:alert(1)", false)]
    [InlineData("java\nscript:alert(1)", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("data:text/html,<script>", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void IsSafeUrl_AllowsOnlyWebMailAndRelativeTargets(string url, bool expected)
    {
        HtmlExport.IsSafeUrl(url).Should().Be(expected);
    }

    /// <summary>
    /// Escaped payload text ("&amp;lt;img onerror=...") is inert and allowed; what must never
    /// appear is a real tag carrying script, an event-handler attribute, or a script URL.
    /// </summary>
    private static void AssertNoExecutableMarkup(string html)
    {
        var lower = html.ToLowerInvariant();
        lower.Should().NotContain("<script");
        lower.Should().NotContain("<img src=x");
        lower.Should().NotMatchRegex("<[a-z][^>]*\\son[a-z]+\\s*=", "no real tag may carry an event handler");
        lower.Should().NotContain("href=\"javascript:");
        lower.Should().NotContain("src=\"javascript:");
        lower.Should().NotContain("href=\"data:");
        lower.Should().NotMatchRegex("href=\"java\\s*script:");
    }
}
