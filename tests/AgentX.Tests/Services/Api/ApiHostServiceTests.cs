using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentX.Core;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Api;
using AgentX.Core.Services.Api.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Settings;
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

    [Fact]
    public async Task StartAsync_WhenThePortIsTaken_ThrowsAndLeavesTheHostStoppedAndStartable()
    {
        await using var owner = await ApiHostHarness.StartAsync();
        await using var second = ApiHostHarness.CreateStopped();

        var act = () => second.Service.StartAsync(owner.Port, DefaultToken);

        await act.Should().ThrowAsync<HttpListenerException>();
        second.Service.IsRunning.Should().BeFalse("a start that failed must not report a running host");
        second.Service.BaseUrl.Should().BeEmpty("the host never listened on the taken port");

        var freePort = ApiHostHarness.GetFreeTcpPort();
        await second.Service.StartAsync(freePort, DefaultToken);

        second.Service.IsRunning.Should().BeTrue();
        second.Service.Port.Should().Be(freePort);
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

    [Fact]
    public async Task SetAuthToken_RevokesThePreviousTokenAndAcceptsTheNewOneWithoutARestart()
    {
        // Regression: the listener captured its token once at StartAsync, so a regenerated token
        // got 401 while the old (possibly leaked) one kept working until the app restarted.
        await using var harness = await ApiHostHarness.StartAsync();
        harness.Collections.Setup(c => c.GetAllCollectionsAsync()).ReturnsAsync(Array.Empty<CollectionEntity>());
        const string regenerated = "REGENERATED-TOKEN-FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";

        harness.Service.SetAuthToken(regenerated);

        var withOldToken = await harness.Client.GetAsync("api/collections");
        harness.SetAuthToken(regenerated);
        var withNewToken = await harness.Client.GetAsync("api/collections");

        withOldToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the replaced token must stop working at once");
        withNewToken.StatusCode.Should().Be(HttpStatusCode.OK, "the new token must work without restarting the listener");
        harness.Service.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task SetAuthToken_Null_FailsClosedOnDataRoutes()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        harness.Service.SetAuthToken(null);
        var response = await harness.Client.GetAsync("api/collections");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ----------------------------------------------------------------------
    //  GET /api/auth/check
    // ----------------------------------------------------------------------

    [Fact]
    public async Task GetAuthCheck_WithValidToken_ConfirmsAuthentication()
    {
        await using var harness = await ApiHostHarness.StartAsync();

        var response = await harness.Client.GetAsync("api/auth/check");
        var body = await ReadAsync<ApiAuthCheckDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Authenticated.Should().BeTrue();
        body.Data.Version.Should().Be(AppVersionInfo.Display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("WRONG-TOKEN")]
    public async Task GetAuthCheck_WithMissingOrWrongToken_Returns401(string? token)
    {
        // Pairing validates against this route; unlike the public health probe it must reject a bad token.
        await using var harness = await ApiHostHarness.StartAsync();
        if (token is null)
            harness.RemoveAuthHeader();
        else
            harness.SetAuthToken(token);

        var response = await harness.Client.GetAsync("api/auth/check");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        body.Data.Version.Should().Be(AppVersionInfo.Display, "the health payload reports the real build, not a hardcoded 1.0.0");
        body.Data.DocumentCount.Should().Be(7);
        body.Data.ConversationCount.Should().Be(3);
        body.Data.Uptime.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHealth_NeverRunsItsTwoDatabaseQueriesAtTheSameTime()
    {
        // Regression: both counts were started with Task.Run in parallel on the one shared
        // AgentXDbContext, which EF Core rejects ("A second operation was started on this
        // context"), so the mobile connectivity check failed intermittently with a 500.
        await using var harness = await ApiHostHarness.StartAsync();
        var inFlight = 0;
        var maxInFlight = 0;

        async Task<T> TrackAsync<T>(T result)
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref inFlight);
            return result;
        }

        harness.Documents.Setup(d => d.GetTotalDocumentCountAsync()).Returns(() => TrackAsync(7L));
        harness.Conversations.Setup(c => c.GetConversationCountAsync()).Returns(() => TrackAsync(3));

        var response = await harness.Client.GetAsync("api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        maxInFlight.Should().Be(1, "the shared DbContext allows only one operation at a time");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
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
    public async Task PostClip_WithABodyOverTheLimit_Returns413AndClipsNothing()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var clipPath = harness.CaptureClipPath();

        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Too big",
            content = new string('x', ApiHostService.MaxRequestBodyBytes),
            sourceUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadAsync<object>(response))!.Success.Should().BeFalse();
        clipPath().Should().BeNull("an oversized request must not reach the inbox");
    }

    [Fact]
    public async Task PostClip_WithABodyJustUnderTheLimit_IsAccepted()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var clipPath = harness.CaptureClipPath();

        // The JSON wrapper around the content adds well under 1 KB.
        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Large clip",
            content = new string('x', ApiHostService.MaxRequestBodyBytes - 1024),
            sourceUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        clipPath().Should().NotBeNull();
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
            written.Should().Contain("\"category\": \"tech\"");
            written.Should().Contain("\"readingMinutes\": \"8\"");
        }
        finally
        {
            if (capturedPath is not null && File.Exists(capturedPath))
                File.Delete(capturedPath);
        }
    }

    [Theory]
    [InlineData("2024-03-05T10:00:00+0000", "2024-03-05")]
    [InlineData("2024-03-05 10:00:00", "2024-03-05")]
    [InlineData("2024-03", "2024-03-01")]
    [InlineData("20240305", "2024-03-05")]
    [InlineData("Tue, 05 Mar 2024 10:00:00 GMT", "2024-03-05")]
    [InlineData("2024-03-05T23:30:00-08:00", "2024-03-05")]
    public async Task PostClip_WithNonIsoPublishedDate_IsAcceptedAndNormalized(string raw, string expected)
    {
        // Regression: PublishedDate was a DateTime?, so every one of these real-world meta/<time>
        // values failed the whole clip with 400 "Invalid JSON in request body".
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        var response = await harness.PostJsonAsync("api/inbox/clip",
            new { title = "Dated", content = "body", sourceUrl = "https://example.com", publishedDate = raw });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var written = await File.ReadAllTextAsync(capturedPath()!);
        written.Should().Contain($"published_date: \"{expected}\"");
    }

    [Theory]
    [InlineData("{\"when\":\"soon\"}")]
    [InlineData("[2024, 3, 5]")]
    [InlineData("true")]
    [InlineData("\"yesterday-ish\"")]
    [InlineData("\"2024-13-45\"")]
    public async Task PostClip_WithUnreadablePublishedDate_IsAcceptedWithoutADate(string publishedDateJson)
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        using var content = new StringContent(
            "{\"title\":\"t\",\"content\":\"body\",\"sourceUrl\":\"u\",\"publishedDate\":" + publishedDateJson + "}",
            Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/inbox/clip", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a bad date must never fail the clip");
        (await File.ReadAllTextAsync(capturedPath()!)).Should().NotContain("published_date");
    }

    [Fact]
    public async Task PostClip_WithNumericCompactPublishedDate_IsAcceptedAndNormalized()
    {
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        using var content = new StringContent(
            "{\"title\":\"t\",\"content\":\"body\",\"sourceUrl\":\"u\",\"publishedDate\":20240305}",
            Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/inbox/clip", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await File.ReadAllTextAsync(capturedPath()!)).Should().Contain("published_date: \"2024-03-05\"");
    }

    [Fact]
    public async Task PostClip_SameTitleWithinOneSecond_KeepsEveryClip()
    {
        // Regression: the file name was title + timestamp to the second, so "Clip All Tabs" with
        // same-title tabs overwrote the first clip, and the inbox (which de-duplicates by path)
        // kept a single item pointing at the last one.
        await using var harness = await ApiHostHarness.StartAsync();
        var paths = new List<string>();
        harness.Inbox
            .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long?, string?, string?>((path, _, _, _) => paths.Add(path))
            .ReturnsAsync(new InboxItemEntity { Id = 1 });

        var first = await harness.PostJsonAsync("api/inbox/clip", new { title = "Same Title", content = "first body", sourceUrl = "u1" });
        var second = await harness.PostJsonAsync("api/inbox/clip", new { title = "Same Title", content = "second body", sourceUrl = "u2" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        paths.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        (await File.ReadAllTextAsync(paths[0])).Should().Contain("first body");
        (await File.ReadAllTextAsync(paths[1])).Should().Contain("second body");
    }

    [Fact]
    public async Task PostClip_WithNullTitleAndNullMetadataValue_Returns201()
    {
        // Regression: a JSON null title or metadata value threw NullReferenceException (500).
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        using var content = new StringContent(
            "{\"title\":null,\"content\":\"body\",\"sourceUrl\":null,\"metadata\":{\"kept\":\"yes\",\"dropped\":null}}",
            Encoding.UTF8, "application/json");
        var response = await harness.Client.PostAsync("api/inbox/clip", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var written = await File.ReadAllTextAsync(capturedPath()!);
        written.Should().Contain("title: \"Untitled\"");
        written.Should().Contain("\"kept\": \"yes\"");
        written.Should().NotContain("dropped");
        Path.GetFileName(capturedPath()!).Should().StartWith("untitled");
    }

    [Fact]
    public async Task PostClip_EscapesNewlinesQuotesAndBackslashes_SoValuesCannotEscapeTheFrontMatter()
    {
        // Regression: values were written raw, so a newline in a title ended the front matter
        // early ("\n---") and let the remainder inject keys of its own.
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        var response = await harness.PostJsonAsync("api/inbox/clip", new
        {
            title = "Line one\n---\ninjected: true",
            content = "body",
            sourceUrl = "https://example.com/a\"b\\c",
            metadata = new Dictionary<string, string> { ["key: with colon"] = "v\r\nw", ["title"] = "shadow" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var written = await File.ReadAllTextAsync(capturedPath()!);
        written.Should().Contain("title: \"Line one\\n---\\ninjected: true\"");
        written.Should().Contain("source_url: \"https://example.com/a\\\"b\\\\c\"");
        written.Should().Contain("\"key: with colon\": \"v\\r\\nw\"");
        written.Should().NotContain("shadow", "metadata must not redefine a key the host writes itself");

        // Exactly two delimiter lines: the front matter opens and closes once.
        written.Split('\n').Select(l => l.TrimEnd('\r')).Count(l => l == "---").Should().Be(2);
    }

    [Fact]
    public async Task PostClip_WithVeryLongTitle_CapsTheFileNameLength()
    {
        // Regression: a 300-character title produced a path past MAX_PATH and failed with 500.
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();
        var longTitle = string.Concat(Enumerable.Repeat("Very long headline segment ", 12));

        var response = await harness.PostJsonAsync("api/inbox/clip", new { title = longTitle, content = "body", sourceUrl = "u" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileName = Path.GetFileName(capturedPath()!);
        fileName.Length.Should().BeLessThanOrEqualTo(ApiHostService.MaxClipFileStemLength + 30);
        fileName.Should().StartWith("Very long headline segment");
        (await File.ReadAllTextAsync(capturedPath()!)).Should().Contain(longTitle.Trim(), "the full title stays in the front matter");
    }

    [Fact]
    public async Task PostClip_StoresTheClipUnderTheAppDataClipsFolderNotTemp()
    {
        // Regression: clips were written under %TEMP%, which Storage Sense and disk cleanup delete
        // while the inbox item still points at the file.
        await using var harness = await ApiHostHarness.StartAsync();
        var capturedPath = harness.CaptureClipPath();

        var response = await harness.PostJsonAsync("api/inbox/clip", new { title = "Kept", content = "body", sourceUrl = "u" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        Path.GetDirectoryName(capturedPath()!).Should().Be(harness.AppPaths.ClipsDirectory);
    }

    [Fact]
    public void BuildClipMarkdownAndFileName_UseGregorianInvariantDatesUnderAnyCulture()
    {
        // Regression: dates were formatted with the current culture's calendar, so th-TH wrote
        // Buddhist-era years (2569) and ar-SA Hijri years into machine-readable fields and names.
        var clippedAt = new DateTime(2026, 3, 15, 1, 2, 3, DateTimeKind.Utc);
        var clip = new ApiClipRequest { Title = "T", Content = "body", SourceUrl = "u", PublishedDate = "2026-03-14", WordCount = 1234 };
        var original = CultureInfo.CurrentCulture;

        foreach (var culture in new[] { "th-TH", "ar-SA" })
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                var markdown = ApiHostService.BuildClipMarkdown(clip, clippedAt);
                var fileName = ApiHostService.BuildClipFileName(clip.Title, clippedAt);

                markdown.Should().Contain("published_date: \"2026-03-14\"", culture);
                markdown.Should().Contain("clipped_at: \"2026-03-15T01:02:03.0000000Z\"", culture);
                markdown.Should().Contain("word_count: 1234", culture);
                fileName.Should().StartWith("T-20260315-010203-", culture);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("10:00")]
    [InlineData("5 March")]
    [InlineData("2024-13-45")]
    public void ParsePublishedDate_ReturnsNullForValuesThatAreNotDates(string? raw)
        => ApiHostService.ParsePublishedDate(raw).Should().BeNull();

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

    [Fact]
    public async Task GetExtensionHealth_ReportsTheRealVersionAndTheConfiguredProvider()
    {
        // Regression: the payload hardcoded version "1.4.0" and provider "local" whatever the
        // build and the user's settings were.
        await using var harness = await ApiHostHarness.StartAsync();
        harness.CurrentSettings.ActiveProviderId = "ollama";

        var response = await harness.Client.GetAsync("api/extension/health");
        var body = await ReadAsync<ApiExtensionHealthDto>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Version.Should().Be(AppVersionInfo.Display);
        body.Data.Provider.Should().Be("ollama");
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
        public Mock<ISettingsService> Settings { get; } = new();
        public AppSettings CurrentSettings { get; } = new();
        public TempAppPathService AppPaths { get; } = new();

        public ApiHostService Service { get; }
        public HttpClient Client { get; private set; } = null!;
        public int Port { get; private set; }

        private ApiHostHarness()
        {
            Settings.Setup(s => s.GetSettingsAsync()).ReturnsAsync(() => CurrentSettings);
            Service = new ApiHostService(
                Conversations.Object, Documents.Object, Collections.Object, Search.Object, Inbox.Object,
                Settings.Object, AppPaths);
        }

        /// <summary>Creates a harness whose host has not been started.</summary>
        public static ApiHostHarness CreateStopped() => new();

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

        /// <summary>
        /// Makes inbox ingestion succeed and records the clip file path it was handed; the returned
        /// accessor yields the most recent path.
        /// </summary>
        public Func<string?> CaptureClipPath()
        {
            string? captured = null;
            Inbox
                .Setup(i => i.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Callback<string, long?, string?, string?>((path, _, _, _) => captured = path)
                .ReturnsAsync(new InboxItemEntity { Id = 1 });
            return () => captured;
        }

        public void RemoveAuthHeader() => Client.DefaultRequestHeaders.Authorization = null;

        public void SetAuthToken(string token) =>
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        public static int GetFreeTcpPort()
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
            AppPaths.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IAppPathService"/> rooted at a disposable per-harness directory, so clip files
    /// never land in the real user profile (AX-QA-011).
    /// </summary>
    private sealed class TempAppPathService : IAppPathService, IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "agentx-api-tests-" + Guid.NewGuid().ToString("N"));

        public string ClipsDirectory => Path.Combine(Root, ApiHostService.ClipsFolderName);

        public string GetAppDataPath() => Directory.CreateDirectory(Root).FullName;

        public string GetTempPath() => Directory.CreateDirectory(Path.Combine(Root, "Temp")).FullName;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a file still held open must not fail the test run.
            }
        }
    }
}
