using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Api;
using AgentX.Core.Services.Api.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Api;

/// <summary>
/// In-process integration tests for <see cref="ApiHostService"/> — the embedded local REST API
/// (AX-QA-009: this service sat at 0% coverage). Each test starts a real <see cref="HttpListener"/>
/// on its own free <c>localhost</c> port and drives it with a real <see cref="HttpClient"/>, so the
/// full request pipeline (auth gate, CORS, routing, JSON serialization, error handling) is exercised
/// end-to-end. The five collaborating services are mocked with Moq; the host depends on Serilog's
/// static logger, which is silent by default, so no logger needs to be supplied.
/// </summary>
public sealed class ApiHostServiceTests
{
    private const string DefaultToken = "TEST-TOKEN-0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF01234567";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // ══════════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StartAsync_SetsRunningStatePortAndBaseUrl()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        harness.Service.IsRunning.Should().BeTrue();
        harness.Service.Port.Should().Be(harness.Port);
        harness.Service.BaseUrl.Should().Be($"http://localhost:{harness.Port}/");
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_IsNoOpAndKeepsSamePort()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var originalPort = harness.Service.Port;

        // Second start on a different port must be ignored (idempotent guard).
        await harness.Service.StartAsync(originalPort + 1, DefaultToken);

        harness.Service.IsRunning.Should().BeTrue();
        harness.Service.Port.Should().Be(originalPort, "a second StartAsync while running must be a no-op");
    }

    [Fact]
    public async Task StopAsync_ClearsRunningState()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        await harness.Service.StopAsync();

        harness.Service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        await harness.Service.StopAsync();

        var act = () => harness.Service.StopAsync();

        await act.Should().NotThrowAsync("stopping an already-stopped host must be idempotent");
    }

    [Fact]
    public async Task DisposeAsync_StopsTheListener()
    {
        var harness = await ApiHostHarness.StartAsync();

        await harness.Service.DisposeAsync();

        harness.Service.IsRunning.Should().BeFalse();
        harness.Client.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Authentication
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProtectedRoute_WithoutAuthorizationHeader_Returns401WithBearerChallenge()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.RemoveAuthHeader();

        var response = await harness.Client.GetAsync("api/collections");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().Contain(h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task ProtectedRoute_WithWrongToken_Returns401()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.SetAuthToken("WRONG-TOKEN");

        var response = await harness.Client.GetAsync("api/collections");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_WithCorrectToken_IsAuthorized()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Collections
            .Setup(c => c.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());

        var response = await harness.Client.GetAsync("api/collections");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicExtensionHealthRoute_RequiresNoToken()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.RemoveAuthHeader();

        var response = await harness.Client.GetAsync("api/extension/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HostStartedWithoutToken_FailsClosedOnDataRoutesButServesPublicProbe()
    {
        // Starting without a token must lock down every data route (fail closed) while leaving the
        // unauthenticated extension health probe reachable so the extension can still detect AgentX.
        await using var harness = await ApiHostHarness.StartAsync(token: null);

        var dataRoute = await harness.Client.GetAsync("api/collections");
        var publicRoute = await harness.Client.GetAsync("api/extension/health");

        dataRoute.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        publicRoute.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CORS
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OptionsPreflight_FromExtensionOrigin_Returns204WithCorsGrant()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        const string origin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";

        using var request = new HttpRequestMessage(HttpMethod.Options, "api/search");
        request.Headers.Add("Origin", origin);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(origin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Should().ContainSingle()
            .Which.Should().Contain("OPTIONS");
    }

    [Fact]
    public async Task OptionsPreflight_WithoutOrigin_Returns204WithoutCorsGrant()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Options, "api/search");
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task GetRequest_FromExtensionOrigin_EchoesCorsOriginAndVary()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Collections.Setup(c => c.GetAllCollectionsAsync()).ReturnsAsync(Array.Empty<CollectionEntity>());
        const string origin = "moz-extension://11112222333344445555666677778888";

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/collections");
        request.Headers.Add("Origin", origin);
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(origin);
        response.Headers.GetValues("Vary").Should().Contain("Origin");
    }

    [Fact]
    public async Task GetRequest_FromWebOrigin_ReceivesNoCorsGrant()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Collections.Setup(c => c.GetAllCollectionsAsync()).ReturnsAsync(Array.Empty<CollectionEntity>());

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/collections");
        request.Headers.Add("Origin", "https://malicious.example.com");
        var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin")
            .Should().BeFalse("a non-extension web origin must never receive a CORS grant");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GET /api/health
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetHealth_ReturnsOkStatusAndCountsFromServices()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Documents.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(7L);
        harness.Conversations.Setup(c => c.GetConversationCountAsync()).ReturnsAsync(3);

        var response = await harness.Client.GetAsync("api/health");
        var body = await ReadAsync<ApiHealthDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data!.Status.Should().Be("ok");
        body.Data.Version.Should().Be("1.0.0");
        body.Data.DocumentCount.Should().Be(7);
        body.Data.ConversationCount.Should().Be(3);
        body.Data.Uptime.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHealth_WithTrailingSlash_StillRoutes()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Documents.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);
        harness.Conversations.Setup(c => c.GetConversationCountAsync()).ReturnsAsync(0);

        var response = await harness.Client.GetAsync("api/health/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GET /api/documents
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDocuments_MapsEntitiesToDtos()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var imported = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        harness.Documents
            .Setup(d => d.GetAllDocumentsAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentEntity>
            {
                new() { Id = 11, FileName = "report.pdf", FileType = "pdf", FileSizeBytes = 2048, ImportedAt = imported, IndexingStatus = "completed" }
            });

        var response = await harness.Client.GetAsync("api/documents");
        var body = await ReadAsync<List<ApiDocumentDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data.Should().ContainSingle();
        var dto = body.Data![0];
        dto.Id.Should().Be(11);
        dto.FileName.Should().Be("report.pdf");
        dto.FileType.Should().Be("pdf");
        dto.FileSizeBytes.Should().Be(2048);
        dto.IndexingStatus.Should().Be("completed");
    }

    [Fact]
    public async Task GetDocumentById_WhenFound_ReturnsDto()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Documents
            .Setup(d => d.GetDocumentAsync(42))
            .ReturnsAsync(new DocumentEntity { Id = 42, FileName = "thesis.docx", FileType = "docx", FileSizeBytes = 99, ImportedAt = DateTime.UtcNow, IndexingStatus = "pending" });

        var response = await harness.Client.GetAsync("api/documents/42");
        var body = await ReadAsync<ApiDocumentDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Id.Should().Be(42);
        body.Data.FileName.Should().Be("thesis.docx");
    }

    [Fact]
    public async Task GetDocumentById_WhenNotFound_Returns404()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Documents.Setup(d => d.GetDocumentAsync(It.IsAny<long>())).ReturnsAsync((DocumentEntity?)null);

        var response = await harness.Client.GetAsync("api/documents/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocumentById_WithNonNumericId_Returns404()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.Client.GetAsync("api/documents/not-a-number");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        harness.Documents.Verify(d => d.GetDocumentAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GetDocuments_WhenServiceThrows_Returns500()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Documents
            .Setup(d => d.GetAllDocumentsAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database offline"));

        var response = await harness.Client.GetAsync("api/documents");
        var body = await ReadAsync<object>(response);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body!.Success.Should().BeFalse();
        body.Error.Should().Contain("internal server error");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GET /api/conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetConversations_MapsEntitiesToDtos()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Conversations
            .Setup(c => c.GetAllConversationsAsync(It.IsAny<bool>()))
            .ReturnsAsync(new List<ConversationEntity>
            {
                new() { Id = 5, Title = "Planning", ModelId = "claude-opus-4-8", MessageCount = 12, TokensUsed = 3400 }
            });

        var response = await harness.Client.GetAsync("api/conversations");
        var body = await ReadAsync<List<ApiConversationDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data.Should().ContainSingle();
        body.Data![0].Id.Should().Be(5);
        body.Data[0].Title.Should().Be("Planning");
        body.Data[0].MessageCount.Should().Be(12);
        body.Data[0].TokensUsed.Should().Be(3400);
    }

    [Fact]
    public async Task GetConversations_RequestsNonArchivedOnly()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Conversations
            .Setup(c => c.GetAllConversationsAsync(It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<ConversationEntity>());

        await harness.Client.GetAsync("api/conversations");

        harness.Conversations.Verify(c => c.GetAllConversationsAsync(false), Times.Once);
    }

    [Fact]
    public async Task GetConversationById_WhenFound_ReturnsDto()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Conversations
            .Setup(c => c.GetConversationAsync(8))
            .ReturnsAsync(new ConversationEntity { Id = 8, Title = "Recall", ModelId = "m" });

        var response = await harness.Client.GetAsync("api/conversations/8");
        var body = await ReadAsync<ApiConversationDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Id.Should().Be(8);
        body.Data.Title.Should().Be("Recall");
    }

    [Fact]
    public async Task GetConversationById_WhenNotFound_Returns404()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Conversations.Setup(c => c.GetConversationAsync(It.IsAny<long>())).ReturnsAsync((ConversationEntity?)null);

        var response = await harness.Client.GetAsync("api/conversations/123");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GET /api/collections
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCollections_MapsEntitiesToDtos()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Collections
            .Setup(c => c.GetAllCollectionsAsync())
            .ReturnsAsync(new List<CollectionEntity>
            {
                new() { Id = 1, Name = "Finance", Description = "Quarterly reports", DocumentCount = 4, CreatedAt = DateTime.UtcNow }
            });

        var response = await harness.Client.GetAsync("api/collections");
        var body = await ReadAsync<List<ApiCollectionDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data.Should().ContainSingle();
        body.Data![0].Name.Should().Be("Finance");
        body.Data[0].Description.Should().Be("Quarterly reports");
        body.Data[0].DocumentCount.Should().Be(4);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POST /api/search
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostSearch_WithValidQuery_ReturnsMappedResults()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Search
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { DocumentId = 3, FileName = "a.pdf", MatchedText = "matched snippet", Score = 0.91f }
            });

        var response = await harness.PostJsonAsync("api/search", new { query = "vector databases", topK = 5, minScore = 0.4 });
        var body = await ReadAsync<List<ApiSearchResultDto>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data.Should().ContainSingle();
        body.Data![0].DocumentId.Should().Be(3);
        body.Data[0].FileName.Should().Be("a.pdf");
        body.Data[0].ChunkContent.Should().Be("matched snippet");
        body.Data[0].Score.Should().BeApproximately(0.91f, 0.0001f);
    }

    [Theory]
    [InlineData(999, 5.0, 50, 1.0f)]   // upper bounds: TopK clamps to 50, MinScore clamps to 1.0
    [InlineData(0, -1.0, 1, 0.0f)]     // lower bounds: TopK clamps to 1,  MinScore clamps to 0.0
    public async Task PostSearch_ClampsTopKAndMinScoreIntoValidRange(
        int requestedTopK, double requestedMinScore, int expectedTopK, float expectedMinScore)
    {
        await using var harness = await ApiHostHarness.StartAsync();
        SearchQuery? captured = null;
        harness.Search
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(Array.Empty<SearchResult>());

        await harness.PostJsonAsync("api/search", new { query = "edge", topK = requestedTopK, minScore = requestedMinScore });

        captured.Should().NotBeNull();
        captured!.TopK.Should().Be(expectedTopK);
        captured.MinScore.Should().Be(expectedMinScore);
        captured.QueryText.Should().Be("edge");
        captured.Mode.Should().Be(SearchMode.Semantic);
    }

    [Fact]
    public async Task PostSearch_WithMalformedJson_Returns400()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        using var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/search", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Search.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostSearch_WithEmptyQuery_Returns400(string query)
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.PostJsonAsync("api/search", new { query });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Search.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostSearch_WithNullJsonBody_Returns400()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        // A literal "null" body deserializes to a null request object — the handler must reject it.
        using var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/search", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Search.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POST /api/inbox/clip
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostClip_WithValidPayload_Returns201AndWritesFrontmatterFileToInbox()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        string? capturedPath = null;
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => capturedPath = path)
            .ReturnsAsync(new InboxItemEntity { Id = 77 });

        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Great Article",
            content = "The body of the clipped content.",
            sourceUrl = "https://example.com/post",
            author = "Jane Doe",
            clipMode = "reader",
            wordCount = 6
        });
        var body = await ReadAsync<ApiClipResponse>(response);

        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            body!.Data!.InboxItemId.Should().Be(77);
            body.Data.Status.Should().Be("clipped");

            harness.Inbox.Verify(i => i.AddToInboxAsync(
                It.IsAny<string>(), null, "browser-extension", "https://example.com/post"), Times.Once);

            capturedPath.Should().NotBeNull();
            File.Exists(capturedPath!).Should().BeTrue("the clip must be persisted for the inbox to ingest");
            var written = await File.ReadAllTextAsync(capturedPath!);
            written.Should().Contain("title: \"Great Article\"");
            written.Should().Contain("source_url: \"https://example.com/post\"");
            written.Should().Contain("author: \"Jane Doe\"");
            written.Should().Contain("clip_mode: reader");
            written.Should().Contain("The body of the clipped content.");
        }
        finally
        {
            if (capturedPath is not null && File.Exists(capturedPath))
                File.Delete(capturedPath);
        }
    }

    [Fact]
    public async Task PostClip_WhenInboxIngestionFails_Returns500AndCleansUpTempFile()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        string? capturedPath = null;
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => capturedPath = path)
            .ThrowsAsync(new InvalidOperationException("inbox unavailable"));

        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Doomed Clip",
            content = "content",
            sourceUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        capturedPath.Should().NotBeNull();
        File.Exists(capturedPath!).Should().BeFalse("a failed ingestion must clean up the temp clip file");
    }

    [Fact]
    public async Task PostClip_WithWhitespaceTitle_FallsBackToUntitledFileName()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        string? capturedPath = null;
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => capturedPath = path)
            .ReturnsAsync(new InboxItemEntity { Id = 1 });

        var response = await harness.PostJsonAsync("api/inbox/clip", new { title = "   ", content = "body", sourceUrl = "u" });

        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            Path.GetFileName(capturedPath!).Should().StartWith("untitled");
        }
        finally
        {
            if (capturedPath is not null && File.Exists(capturedPath))
                File.Delete(capturedPath);
        }
    }

    [Fact]
    public async Task PostClip_SanitizesInvalidFileNameCharactersFromTitle()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        string? capturedPath = null;
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => capturedPath = path)
            .ReturnsAsync(new InboxItemEntity { Id = 1 });

        var response = await harness.PostJsonAsync("api/inbox/clip",
            new { title = "Q4/Report:2026*Final", content = "body", sourceUrl = "u" });

        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var fileName = Path.GetFileName(capturedPath!);
            fileName.Should().NotContainAny("/", "\\", ":", "*", "?", "\"", "<", ">", "|");
            fileName.Should().StartWith("Q4_Report_2026_Final");
        }
        finally
        {
            if (capturedPath is not null && File.Exists(capturedPath))
                File.Delete(capturedPath);
        }
    }

    [Fact]
    public async Task PostClip_WithMalformedJson_Returns400()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        using var content = new StringContent("}{", Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/inbox/clip", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Inbox.Verify(i => i.AddToInboxAsync(
            It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PostClip_WithEmptyContent_Returns400()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.PostJsonAsync("api/inbox/clip", new { title = "t", content = "", sourceUrl = "u" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Inbox.Verify(i => i.AddToInboxAsync(
            It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PostClip_WithNullJsonBody_Returns400()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        using var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/inbox/clip", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        harness.Inbox.Verify(i => i.AddToInboxAsync(
            It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PostClip_WithPublishedDateAndMetadata_EmbedsThemInFrontmatter()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        string? capturedPath = null;
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => capturedPath = path)
            .ReturnsAsync(new InboxItemEntity { Id = 1 });

        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Dated Clip",
            content = "body",
            sourceUrl = "https://example.com",
            publishedDate = "2026-03-15T12:00:00",
            metadata = new Dictionary<string, string> { ["category"] = "tech", ["readingMinutes"] = "8" }
        });

        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            capturedPath.Should().NotBeNull();
            var written = await File.ReadAllTextAsync(capturedPath!);
            written.Should().Contain("published_date: \"2026-03-15\"");
            written.Should().Contain("category: \"tech\"");
            written.Should().Contain("readingMinutes: \"8\"");
        }
        finally
        {
            if (capturedPath is not null && File.Exists(capturedPath))
                File.Delete(capturedPath);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GET /api/extension/health
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetExtensionHealth_ReturnsConnectedPayload()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.Client.GetAsync("api/extension/health");
        var body = await ReadAsync<ApiExtensionHealthDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Connected.Should().BeTrue();
        body.Data.InboxEnabled.Should().BeTrue();
        body.Data.Provider.Should().Be("local");
        body.Data.Version.Should().NotBeNullOrWhiteSpace();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Routing fallbacks
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.Client.GetAsync("api/does-not-exist");
        var body = await ReadAsync<object>(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body!.Success.Should().BeFalse();
        body.Error.Should().Contain("Route not found");
    }

    [Fact]
    public async Task KnownPathWithWrongMethod_Returns404()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        // /api/health only answers GET; a POST falls through to the 404 fallback.
        var response = await harness.PostJsonAsync("api/health", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private static async Task<ApiResponse<T>?> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<ApiResponse<T>>(json, ReadOptions);
    }

    /// <summary>
    /// Owns a live <see cref="ApiHostService"/> bound to a free localhost port plus an
    /// <see cref="HttpClient"/> pre-authorized with the host's bearer token. Disposing the harness
    /// stops the listener and releases the client.
    /// </summary>
    private sealed class ApiHostHarness : IAsyncDisposable
    {
        public Mock<IConversationService> Conversations { get; } = new();
        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<ICollectionService> Collections { get; } = new();
        public Mock<ISemanticSearchService> Search { get; } = new();
        public Mock<IInboxService> Inbox { get; } = new();

        public ApiHostService Service { get; }
        public HttpClient Client { get; private set; } = null!;
        public int Port { get; private set; }

        private ApiHostHarness() =>
            Service = new ApiHostService(
                Conversations.Object, Documents.Object, Collections.Object, Search.Object, Inbox.Object);

        public static async Task<ApiHostHarness> StartAsync(string? token = DefaultToken)
        {
            var harness = new ApiHostHarness();
            harness.Port = GetFreeTcpPort();
            await harness.Service.StartAsync(harness.Port, token);

            harness.Client = new HttpClient { BaseAddress = new Uri(harness.Service.BaseUrl) };
            if (!string.IsNullOrEmpty(token))
                harness.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return harness;
        }

        public Task<HttpResponseMessage> PostJsonAsync(string path, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return Client.PostAsync(path, content);
        }

        public void RemoveAuthHeader() => Client.DefaultRequestHeaders.Authorization = null;

        public void SetAuthToken(string token) =>
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            await Service.StopAsync();
        }
    }
}
