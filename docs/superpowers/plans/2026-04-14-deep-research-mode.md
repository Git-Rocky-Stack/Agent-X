# Deep Research Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional Research Mode that supplements local vault knowledge with web search results, clearly tagged as [Vault] vs [Web], with one-click "Save to Vault" for web sources and configurable search API providers.

**Architecture:** Create `IWebSearchService` with three provider implementations (Brave, Serper, SearXNG). Add Research Mode toggle to `AppSettings` and `ChatViewModel`. Modify `RagPipeline.AskAsync` to optionally enrich context with web search results. Tag citations as local vs. web. Add "Save to Vault" action that sends web results to Smart Inbox via `IInboxService`. Cache search results with configurable TTL. Track search API costs in `CostTracker`.

**Tech Stack:** C#, .NET 8, HttpClient for search APIs, System.Text.Json for deserialization, xUnit

---

### Task 1: Web Search Service Interface and Models

**Files:**
- Create: `src/AgentX.Core/Services/Search/IWebSearchService.cs`
- Create: `src/AgentX.Core/Services/Search/WebSearchModels.cs`
- Modify: `src/AgentX.Core/Services/Settings/AppSettings.cs`
- Test: `tests/AgentX.Tests/Services/Search/WebSearchModelsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Search/WebSearchModelsTests.cs
using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class WebSearchModelsTests
{
    [Fact]
    public void WebSearchResult_HasRequiredFields()
    {
        var result = new WebSearchResult
        {
            Title = "Test Article",
            Url = "https://example.com/article",
            Snippet = "A test snippet about something",
            SourceDomain = "example.com",
            PublishedDate = new DateTime(2026, 4, 14)
        };

        result.Title.Should().Be("Test Article");
        result.Url.Should().Be("https://example.com/article");
        result.Snippet.Should().Be("A test snippet about something");
        result.SourceDomain.Should().Be("example.com");
    }

    [Fact]
    public void WebSearchResponse_CalculatesTotalResults()
    {
        var response = new WebSearchResponse
        {
            Query = "AI in healthcare",
            Results = new List<WebSearchResult>
            {
                new() { Title = "Result 1", Url = "https://1.com" },
                new() { Title = "Result 2", Url = "https://2.com" },
                new() { Title = "Result 3", Url = "https://3.com" }
            },
            SearchProvider = WebSearchProvider.Brave
        };

        response.Results.Should().HaveCount(3);
        response.SearchProvider.Should().Be(WebSearchProvider.Brave);
    }

    [Fact]
    public void AppSettings_HasResearchModeFields()
    {
        var settings = new AppSettings
        {
            EnableResearchMode = true,
            WebSearchProvider = WebSearchProvider.Brave,
            WebSearchApiKey = "test-key",
            MaxSearchResults = 10,
            SearchCacheTtlMinutes = 60
        };

        settings.EnableResearchMode.Should().BeTrue();
        settings.WebSearchProvider.Should().Be(WebSearchProvider.Brave);
        settings.WebSearchApiKey.Should().Be("test-key");
        settings.MaxSearchResults.Should().Be(10);
        settings.SearchCacheTtlMinutes.Should().Be(60);
    }

    [Fact]
    public void WebSearchProvider_Enum_HasThreeProviders()
    {
        ((int)WebSearchProvider.Brave).Should().BeGreaterOrEqualTo(0);
        ((int)WebSearchProvider.Serper).Should().BeGreaterOrEqualTo(0);
        ((int)WebSearchProvider.SearXng).Should().BeGreaterOrEqualTo(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AgentX.Tests --filter "WebSearchModelsTests" -v n -r win-x64`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Create WebSearchProvider enum and models**

```csharp
// src/AgentX.Core/Services/Search/WebSearchModels.cs
namespace AgentX.Core.Services.Search;

public enum WebSearchProvider
{
    Brave,
    Serper,
    SearXng
}

public sealed class WebSearchResult
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public string SourceDomain { get; init; } = string.Empty;
    public DateTime? PublishedDate { get; init; }
    public string? RawContent { get; init; }
}

public sealed class WebSearchResponse
{
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<WebSearchResult> Results { get; init; } = Array.Empty<WebSearchResult>();
    public WebSearchProvider SearchProvider { get; init; }
    public TimeSpan SearchDuration { get; init; }
    public bool FromCache { get; init; }
}
```

- [ ] **Step 4: Create IWebSearchService interface**

```csharp
// src/AgentX.Core/Services/Search/IWebSearchService.cs
using AgentX.Core.Services.Search;

namespace AgentX.Core.Services.Search;

public interface IWebSearchService
{
    Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);
    bool IsConfigured { get; }
    WebSearchProvider ActiveProvider { get; }
}
```

- [ ] **Step 5: Add Research Mode fields to AppSettings**

```csharp
// Add to src/AgentX.Core/Services/Settings/AppSettings.cs:
public bool EnableResearchMode { get; set; } = false;
public WebSearchProvider WebSearchProvider { get; set; } = WebSearchProvider.Brave;
public string? WebSearchApiKey { get; set; }
public int MaxSearchResults { get; set; } = 10;
public int SearchCacheTtlMinutes { get; set; } = 60;
```

Note: The implementer should add `using AgentX.Core.Services.Search;` to `AppSettings.cs`.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/AgentX.Tests --filter "WebSearchModelsTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/AgentX.Core/Services/Search/IWebSearchService.cs src/AgentX.Core/Services/Search/WebSearchModels.cs src/AgentX.Core/Services/Settings/AppSettings.cs tests/AgentX.Tests/Services/Search/WebSearchModelsTests.cs
git commit -m "feat(research): add web search service interface, models, and AppSettings fields"
```

---

### Task 2: Web Search Provider Implementations (Brave, Serper, SearXNG)

**Files:**
- Create: `src/AgentX.Core/Services/Search/BraveSearchService.cs`
- Create: `src/AgentX.Core/Services/Search/SerperSearchService.cs`
- Create: `src/AgentX.Core/Services/Search/SearXngSearchService.cs`
- Create: `src/AgentX.Core/Services/Search/WebSearchCache.cs`
- Test: `tests/AgentX.Tests/Services/Search/BraveSearchServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Search/BraveSearchServiceTests.cs
using AgentX.Core.Services.Search;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class BraveSearchServiceTests
{
    [Fact]
    public void IsConfigured_WithApiKey_ReturnsTrue()
    {
        var service = new BraveSearchService("test-api-key");
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_WithoutApiKey_ReturnsFalse()
    {
        var service = new BraveSearchService(null);
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void ActiveProvider_ReturnsBrave()
    {
        var service = new BraveSearchService("test-key");
        service.ActiveProvider.Should().Be(WebSearchProvider.Brave);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AgentX.Tests --filter "BraveSearchServiceTests" -v n -r win-x64`
Expected: FAIL — `BraveSearchService` doesn't exist.

- [ ] **Step 3: Implement BraveSearchService**

```csharp
// src/AgentX.Core/Services/Search/BraveSearchService.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentX.Core.Services.Search;

public sealed class BraveSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly WebSearchCache _cache;

    private const string BraveApiBaseUrl = "https://api.search.brave.com/res/v1/web/search";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public WebSearchProvider ActiveProvider => WebSearchProvider.Brave;

    public BraveSearchService(string? apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _httpClient = httpClient ?? new HttpClient();
        _cache = new WebSearchCache();
    }

    public async Task<WebSearchResponse> SearchAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Brave Search API key not configured");

        // Check cache first
        var cached = _cache.Get(query, WebSearchProvider.Brave);
        if (cached != null)
            return cached with { FromCache = true };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var requestUrl = $"{BraveApiBaseUrl}?q={Uri.EscapeDataString(query)}&count={maxResults}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept-Encoding", "gzip");
        request.Headers.Add("X-Subscription-Token", _apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var braveResponse = JsonSerializer.Deserialize<BraveSearchApiResponse>(json);

        sw.Stop();

        var results = braveResponse?.Web?.Results?
            .Select(r => new WebSearchResult
            {
                Title = r.Title ?? string.Empty,
                Url = r.Url ?? string.Empty,
                Snippet = r.Description ?? string.Empty,
                SourceDomain = ExtractDomain(r.Url),
                PublishedDate = r.PageAge != null ? ParseDate(r.PageAge) : null,
                RawContent = null
            })
            .Take(maxResults)
            .ToList() ?? new List<WebSearchResult>();

        var searchResponse = new WebSearchResponse
        {
            Query = query,
            Results = results,
            SearchProvider = WebSearchProvider.Brave,
            SearchDuration = sw.Elapsed,
            FromCache = false
        };

        _cache.Set(query, WebSearchProvider.Brave, searchResponse);
        return searchResponse;
    }

    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        try { return new Uri(url).Host; }
        catch { return string.Empty; }
    }

    private static DateTime? ParseDate(string dateStr)
    {
        return DateTime.TryParse(dateStr, out var date) ? date : null;
    }

    // JSON response models for Brave Search API
    private sealed class BraveSearchApiResponse
    {
        public BraveSearchWeb? Web { get; set; }
    }

    private sealed class BraveSearchWeb
    {
        public List<BraveSearchResult>? Results { get; set; }
    }

    private sealed class BraveSearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
        public string? PageAge { get; set; }
    }
}
```

- [ ] **Step 4: Implement SerperSearchService**

```csharp
// src/AgentX.Core/Services/Search/SerperSearchService.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentX.Core.Services.Search;

public sealed class SerperSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly WebSearchCache _cache;

    private const string SerperApiBaseUrl = "https://google.serper.dev/search";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public WebSearchProvider ActiveProvider => WebSearchProvider.Serper;

    public SerperSearchService(string? apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _httpClient = httpClient ?? new HttpClient();
        _cache = new WebSearchCache();
    }

    public async Task<WebSearchResponse> SearchAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Serper API key not configured");

        var cached = _cache.Get(query, WebSearchProvider.Serper);
        if (cached != null)
            return cached with { FromCache = true };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var payload = new { q = query, num = maxResults };
        using var request = new HttpRequestMessage(HttpMethod.Post, SerperApiBaseUrl);
        request.Headers.Add("X-API-KEY", _apiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var serperResponse = JsonSerializer.Deserialize<SerperSearchApiResponse>(json);

        sw.Stop();

        var results = serperResponse?.Organic?
            .Select(r => new WebSearchResult
            {
                Title = r.Title ?? string.Empty,
                Url = r.Link ?? string.Empty,
                Snippet = r.Snippet ?? string.Empty,
                SourceDomain = ExtractDomain(r.Link),
                PublishedDate = r.Date != null ? ParseDate(r.Date) : null,
                RawContent = null
            })
            .Take(maxResults)
            .ToList() ?? new List<WebSearchResult>();

        var searchResponse = new WebSearchResponse
        {
            Query = query,
            Results = results,
            SearchProvider = WebSearchProvider.Serper,
            SearchDuration = sw.Elapsed,
            FromCache = false
        };

        _cache.Set(query, WebSearchProvider.Serper, searchResponse);
        return searchResponse;
    }

    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        try { return new Uri(url).Host; }
        catch { return string.Empty; }
    }

    private static DateTime? ParseDate(string dateStr)
    {
        return DateTime.TryParse(dateStr, out var date) ? date : null;
    }

    private sealed class SerperSearchApiResponse
    {
        public List<SerperOrganicResult>? Organic { get; set; }
    }

    private sealed class SerperOrganicResult
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? Snippet { get; set; }
        public string? Date { get; set; }
    }
}
```

- [ ] **Step 5: Implement SearXngSearchService**

```csharp
// src/AgentX.Core/Services/Search/SearXngSearchService.cs
using System.Text.Json;

namespace AgentX.Core.Services.Search;

public sealed class SearXngSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _instanceUrl;
    private readonly WebSearchCache _cache;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_instanceUrl);
    public WebSearchProvider ActiveProvider => WebSearchProvider.SearXng;

    public SearXngSearchService(string? instanceUrl, HttpClient? httpClient = null)
    {
        _instanceUrl = instanceUrl ?? string.Empty;
        _httpClient = httpClient ?? new HttpClient();
        _cache = new WebSearchCache();
    }

    public async Task<WebSearchResponse> SearchAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SearXNG instance URL not configured");

        var cached = _cache.Get(query, WebSearchProvider.SearXng);
        if (cached != null)
            return cached with { FromCache = true };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var requestUrl = $"{_instanceUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&categories=general";
        using var response = await _httpClient.GetAsync(requestUrl, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var searxResponse = JsonSerializer.Deserialize<SearXngApiResponse>(json);

        sw.Stop();

        var results = searxResponse?.Results?
            .Where(r => !string.IsNullOrEmpty(r.Url))
            .Select(r => new WebSearchResult
            {
                Title = r.Title ?? string.Empty,
                Url = r.Url ?? string.Empty,
                Snippet = r.Content ?? string.Empty,
                SourceDomain = ExtractDomain(r.Url),
                PublishedDate = r.PublishedDate,
                RawContent = null
            })
            .Take(maxResults)
            .ToList() ?? new List<WebSearchResult>();

        var searchResponse = new WebSearchResponse
        {
            Query = query,
            Results = results,
            SearchProvider = WebSearchProvider.SearXng,
            SearchDuration = sw.Elapsed,
            FromCache = false
        };

        _cache.Set(query, WebSearchProvider.SearXng, searchResponse);
        return searchResponse;
    }

    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        try { return new Uri(url).Host; }
        catch { return string.Empty; }
    }

    private sealed class SearXngApiResponse
    {
        public List<SearXngResult>? Results { get; set; }
    }

    private sealed class SearXngResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
        public DateTime? PublishedDate { get; set; }
    }
}
```

- [ ] **Step 6: Implement WebSearchCache**

```csharp
// src/AgentX.Core/Services/Search/WebSearchCache.cs
using Microsoft.Extensions.Logging;

namespace AgentX.Core.Services.Search;

public sealed class WebSearchCache
{
    private readonly Dictionary<(string Query, WebSearchProvider Provider), (WebSearchResponse Response, DateTime ExpiresAt)> _cache = new();
    private readonly object _lock = new();

    public WebSearchResponse? Get(string query, WebSearchProvider provider)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue((query, provider), out var entry))
            {
                if (DateTime.UtcNow < entry.ExpiresAt)
                    return entry.Response;
                _cache.Remove((query, provider));
            }
            return null;
        }
    }

    public void Set(string query, WebSearchProvider provider, WebSearchResponse response, int? ttlMinutes = null)
    {
        lock (_lock)
        {
            var expiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes ?? 60);
            _cache[(query, provider)] = (response, expiresAt);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/AgentX.Tests --filter "BraveSearchServiceTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/AgentX.Core/Services/Search/BraveSearchService.cs src/AgentX.Core/Services/Search/SerperSearchService.cs src/AgentX.Core/Services/Search/SearXngSearchService.cs src/AgentX.Core/Services/Search/WebSearchCache.cs tests/AgentX.Tests/Services/Search/BraveSearchServiceTests.cs
git commit -m "feat(research): implement Brave, Serper, and SearXNG search providers with caching"
```

---

### Task 3: Research Mode Integration into RAG Pipeline

**Files:**
- Modify: `src/AgentX.Core/Search/RagPipeline.cs`
- Modify: `src/AgentX.Core/Search/IRagPipeline.cs`
- Modify: `src/AgentX.Core/Search/Models/RagResponse.cs`
- Test: `tests/AgentX.Tests/Search/RagPipelineResearchModeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Search/RagPipelineResearchModeTests.cs
using AgentX.Core.Search.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Search;

public class RagPipelineResearchModeTests
{
    [Fact]
    public void RagResponse_WebCitations_PropertyExists()
    {
        var response = new RagResponse
        {
            AnswerText = "Test answer",
            Question = "Test question",
            WebCitations = new List<WebCitation>
            {
                new()
                {
                    Title = "Web Result",
                    Url = "https://example.com",
                    Snippet = "A web snippet",
                    Source = WebCitationSource.Web
                }
            }
        };

        response.WebCitations.Should().NotBeEmpty();
        response.WebCitations![0].Source.Should().Be(WebCitationSource.Web);
    }

    [Fact]
    public void WebCitationSource_HasVaultAndWeb()
    {
        ((int)WebCitationSource.Vault).Should().BeGreaterOrEqualTo(0);
        ((int)WebCitationSource.Web).Should().BeGreaterOrEqualTo(0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AgentX.Tests --filter "RagPipelineResearchModeTests" -v n -r win-x64`
Expected: FAIL — `WebCitation`, `WebCitationSource`, and `RagResponse.WebCitations` don't exist.

- [ ] **Step 3: Add WebCitation model and source enum**

```csharp
// Add to src/AgentX.Core/Search/Models/RagResponse.cs or create a new file:
// src/AgentX.Core/Search/Models/WebCitation.cs
namespace AgentX.Core.Search.Models;

public enum WebCitationSource
{
    Vault,
    Web
}

public sealed class WebCitation
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public WebCitationSource Source { get; init; }
    public string? DocumentName { get; init; }
}
```

- [ ] **Step 4: Add WebCitations to RagResponse**

```csharp
// Add to src/AgentX.Core/Search/Models/RagResponse.cs:
public IReadOnlyList<WebCitation>? WebCitations { get; init; }
```

- [ ] **Step 5: Add Research Mode to IRagPipeline**

```csharp
// Add to src/AgentX.Core/Search/IRagPipeline.cs:
Task<RagResponse> AskAsync(
    string question,
    long? collectionId = null,
    bool enableResearchMode = false,
    Action<string>? onToken = null,
    CancellationToken ct = default);
```

- [ ] **Step 6: Integrate web search into RagPipeline.AskAsync**

Add `IWebSearchService` as an optional dependency to `RagPipeline`:

```csharp
// In RagPipeline constructor, add optional parameter:
private readonly IWebSearchService? _webSearchService;

public RagPipeline(
    ISemanticSearchService semanticSearch,
    IAiService aiService,
    ICitationService citationService,
    IRagReranker reranker,
    AgentXDbContext dbContext,
    ILogger<RagPipeline> logger,
    IMultiQueryGenerator? multiQueryGenerator = null,
    IHydeService? hydeService = null,
    ILlmReranker? llmReranker = null,
    IParentDocumentRetriever? parentDocumentRetriever = null,
    IContextualCompressor? contextualCompressor = null,
    IRagEvaluator? ragEvaluator = null,
    IWebSearchService? webSearchService = null)  // NEW
{
    // ... existing assignments ...
    _webSearchService = webSearchService;
}
```

In the `AskAsync` method, add the `enableResearchMode` parameter and enrich context:

```csharp
public async Task<RagResponse> AskAsync(
    string question,
    long? collectionId = null,
    bool enableResearchMode = false,
    Action<string>? onToken = null,
    CancellationToken ct = default)
{
    // ... existing pipeline steps (multi-query, HyDE, search, rerank) ...

    // After local vault retrieval, optionally enrich with web search:
    List<WebCitation>? webCitations = null;
    string? webContext = null;

    if (enableResearchMode && _webSearchService?.IsConfigured == true)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync(); // if injected
            var searchResponse = await _webSearchService.SearchAsync(
                question, settings.MaxSearchResults, ct);

            if (searchResponse.Results.Count > 0)
            {
                webCitations = searchResponse.Results
                    .Select(r => new WebCitation
                    {
                        Title = r.Title,
                        Url = r.Url,
                        Snippet = r.Snippet,
                        Source = WebCitationSource.Web,
                        DocumentName = r.SourceDomain
                    })
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("## Web Sources (for reference, verify independently):");
                foreach (var result in searchResponse.Results)
                {
                    sb.AppendLine($"- [{result.Title}]({result.Url})");
                    sb.AppendLine($"  {result.Snippet}");
                }
                webContext = sb.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web search failed for query: {Query}", question);
            // Research mode degrades gracefully — local results still work
        }
    }

    // Build the RAG prompt with web context appended
    var prompt = BuildRagPrompt(contextChunks, question, webContext);

    // ... existing streaming, citation extraction, evaluation ...

    return new RagResponse
    {
        // ... existing fields ...
        WebCitations = webCitations
    };
}
```

- [ ] **Step 7: Update BuildRagPrompt to include web context**

Modify the existing `BuildRagPrompt` method (or its equivalent in `RagPipeline`) to accept an optional `webContext` parameter that gets appended to the system prompt:

```csharp
private string BuildRagPrompt(
    string context,
    string question,
    string? webContext = null)
{
    var sb = new StringBuilder();
    sb.AppendLine("You are a knowledgeable assistant with access to both local knowledge base content and web search results.");
    sb.AppendLine();
    sb.AppendLine("## Local Knowledge Base:");
    sb.AppendLine(context);

    if (!string.IsNullOrEmpty(webContext))
    {
        sb.AppendLine(webContext);
    }

    sb.AppendLine();
    sb.AppendLine("## Question:");
    sb.AppendLine(question);
    sb.AppendLine();
    sb.AppendLine("Answer the question using the local knowledge base as your primary source. ");
    sb.AppendLine("Web sources can supplement your answer but should be clearly marked as [Web] when referenced.");
    sb.AppendLine("If local knowledge and web sources conflict, prefer local knowledge and note the discrepancy.");

    return sb.ToString();
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/AgentX.Tests --filter "RagPipelineResearchModeTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/AgentX.Core/Search/RagPipeline.cs src/AgentX.Core/Search/IRagPipeline.cs src/AgentX.Core/Search/Models/RagResponse.cs src/AgentX.Core/Search/Models/WebCitation.cs tests/AgentX.Tests/Search/RagPipelineResearchModeTests.cs
git commit -m "feat(research): integrate web search into RAG pipeline with dual-source citations"
```

---

### Task 4: Research Mode Chat UI Toggle and Settings

**Files:**
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.App/Views/SettingsPage.xaml`
- Modify: `src/AgentX.App/App.xaml.cs`

- [ ] **Step 1: Add Research Mode properties to ChatViewModel**

```csharp
// Add to ChatViewModel:
private bool _isResearchMode;

public bool IsResearchMode
{
    get => _isResearchMode;
    set
    {
        if (SetProperty(ref _isResearchMode, value))
        {
            OnPropertyChanged(nameof(ResearchModeTooltip));
            _notificationService.Show(value
                ? "Research Mode: Web search enabled"
                : "Research Mode: Local vault only");
        }
    }
}

public string ResearchModeTooltip => IsResearchMode
    ? "Research Mode ON — answers include web sources"
    : "Research Mode OFF — local vault only";
```

- [ ] **Step 2: Add Research Mode toggle command to ChatViewModel**

```csharp
[RelayCommand]
private void ToggleResearchMode()
{
    IsResearchMode = !IsResearchMode;
}
```

- [ ] **Step 3: Pass Research Mode to SendMessageAsync**

In the `SendMessageAsync` method, when calling `RagPipeline.AskAsync`, pass `enableResearchMode: IsResearchMode`:

```csharp
// Wherever RagPipeline.AskAsync is called:
var response = await _ragPipeline.AskAsync(
    userMessage,
    collectionId: null,
    enableResearchMode: IsResearchMode,
    onToken: token => { /* streaming callback */ },
    ct: ct);
```

Note: The implementer should check how `ChatService.SendMessageAsync` calls the AI service. If `RagPipeline` is not directly invoked in `ChatService`, the `IsResearchMode` flag needs to flow through `ChatService` to wherever the RAG pipeline is invoked. If the chat path doesn't use RAG at all, Research Mode adds web context to the system prompt via `IWebSearchService` directly.

- [ ] **Step 4: Add Research Mode toggle to ChatPage.xaml input bar**

Add a toggle button next to the voice input buttons in the chat input area:

```xml
<!-- Add in ChatPage.xaml, before the send/stop buttons in the input bar -->
<ToggleButton
    x:Name="ResearchModeToggle"
    ToolTipService.ToolTip="{x:Bind ViewModel.ResearchModeTooltip, Mode=OneWay}"
    IsChecked="{x:Bind ViewModel.IsResearchMode, Mode=TwoWay}"
    Content="&#xE774;"
    FontFamily="Segoe MDL2 Assets"
    Click="ResearchModeToggle_Click" />
```

- [ ] **Step 5: Add Research Mode settings section to SettingsPage.xaml**

In the Settings page, add a "Research Mode" section:

```xml
<!-- Add in SettingsPage.xaml, in the AI/Model settings section -->
<StackPanel Spacing="8" Margin="0,16,0,0">
    <TextBlock Text="Research Mode" Style="{StaticResource SubtitleTextBlockStyle}" />
    <ToggleSwitch
        Header="Enable Research Mode"
        IsOn="{x:Bind ViewModel.EnableResearchMode, Mode=TwoWay}"
        OffContent="Local vault only"
        OnContent="Local vault + web search" />

    <ComboBox
        Header="Search Provider"
        ItemsSource="{x:Bind ViewModel.WebSearchProviders}"
        SelectedItem="{x:Bind ViewModel.SelectedWebSearchProvider, Mode=TwoWay}"
        IsEnabled="{x:Bind ViewModel.EnableResearchMode, Mode=OneWay}" />

    <TextBox
        Header="Search API Key"
        Text="{x:Bind ViewModel.WebSearchApiKey, Mode=TwoWay}"
        IsEnabled="{x:Bind ViewModel.EnableResearchMode, Mode=OneWay}"
        PlaceholderText="Enter your search API key" />

    <NumberBox
        Header="Max Search Results"
        Value="{x:Bind ViewModel.MaxSearchResults, Mode=TwoWay}"
        Minimum="1" Maximum="20" SpinButtonPlacementOption="Compact"
        IsEnabled="{x:Bind ViewModel.EnableResearchMode, Mode=OneWay}" />

    <NumberBox
        Header="Cache Duration (minutes)"
        Value="{x:Bind ViewModel.SearchCacheTtlMinutes, Mode=TwoWay}"
        Minimum="5" Maximum="1440" SpinButtonPlacementOption="Compact"
        IsEnabled="{x:Bind ViewModel.EnableResearchMode, Mode=OneWay}" />
</StackPanel>
```

- [ ] **Step 6: Add SettingsViewModel properties**

```csharp
// Add to SettingsViewModel:
public bool EnableResearchMode
{
    get => _settings.EnableResearchMode;
    set
    {
        if (_settings.EnableResearchMode != value)
        {
            _settings.EnableResearchMode = value;
            OnPropertyChanged();
        }
    }
}

public IReadOnlyList<WebSearchProvider> WebSearchProviders { get; }
    = Enum.GetValues<WebSearchProvider>().ToList();

public WebSearchProvider SelectedWebSearchProvider
{
    get => _settings.WebSearchProvider;
    set
    {
        if (_settings.WebSearchProvider != value)
        {
            _settings.WebSearchProvider = value;
            OnPropertyChanged();
        }
    }
}

public string? WebSearchApiKey
{
    get => _settings.WebSearchApiKey;
    set
    {
        if (_settings.WebSearchApiKey != value)
        {
            _settings.WebSearchApiKey = value;
            OnPropertyChanged();
        }
    }
}

public int MaxSearchResults
{
    get => _settings.MaxSearchResults;
    set
    {
        if (_settings.MaxSearchResults != value)
        {
            _settings.MaxSearchResults = value;
            OnPropertyChanged();
        }
    }
}

public int SearchCacheTtlMinutes
{
    get => _settings.SearchCacheTtlMinutes;
    set
    {
        if (_settings.SearchCacheTtlMinutes != value)
        {
            _settings.SearchCacheTtlMinutes = value;
            OnPropertyChanged();
        }
    }
}
```

- [ ] **Step 7: Register IWebSearchService in DI**

In `App.xaml.cs`, register the web search service based on settings:

```csharp
// In service registration:
services.AddSingleton<IWebSearchService>(sp =>
{
    var settings = sp.GetRequiredService<ISettingsService>().GetSettingsAsync().GetAwaiter().GetResult();
    return settings.WebSearchProvider switch
    {
        WebSearchProvider.Brave => new BraveSearchService(settings.WebSearchApiKey),
        WebSearchProvider.Serper => new SerperSearchService(settings.WebSearchApiKey),
        WebSearchProvider.SearXng => new SearXngSearchService(settings.WebSearchApiKey),
        _ => new BraveSearchService(settings.WebSearchApiKey)
    };
});
```

Note: This factory approach means the service is recreated when settings change. The implementer may want to use a factory pattern that resolves the provider at call time instead of at DI time, to support runtime provider switching. If so, create a `WebSearchServiceFactory`:

```csharp
// src/AgentX.Core/Services/Search/WebSearchServiceFactory.cs
namespace AgentX.Core.Services.Search;

public sealed class WebSearchServiceFactory
{
    private readonly Dictionary<WebSearchProvider, IWebSearchService> _services;

    public WebSearchServiceFactory(
        string? braveApiKey,
        string? serperApiKey,
        string? searxngUrl)
    {
        _services = new Dictionary<WebSearchProvider, IWebSearchService>
        {
            [WebSearchProvider.Brave] = new BraveSearchService(braveApiKey),
            [WebSearchProvider.Serper] = new SerperSearchService(serperApiKey),
            [WebSearchProvider.SearXng] = new SearXngSearchService(searxngUrl)
        };
    }

    public IWebSearchService GetService(WebSearchProvider provider) => _services[provider];

    public IWebSearchService GetConfiguredService(AppSettings settings)
    {
        var service = _services[settings.WebSearchProvider];
        return service.IsConfigured ? service : _services.Values.FirstOrDefault(s => s.IsConfigured) ?? service;
    }
}
```

- [ ] **Step 8: Verify build**

Run: `dotnet build src/AgentX.App -r win-x64`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/AgentX.App/ViewModels/ChatViewModel.cs src/AgentX.App/ViewModels/SettingsViewModel.cs src/AgentX.App/Views/ChatPage.xaml src/AgentX.App/Views/SettingsPage.xaml src/AgentX.App/App.xaml.cs src/AgentX.Core/Services/Search/WebSearchServiceFactory.cs
git commit -m "feat(research): add Research Mode toggle, settings UI, and DI registration"
```

---

### Task 5: Web Citation Display and "Save to Vault" Action

**Files:**
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.App/Views/ChatPage.xaml.cs`
- Modify: `src/AgentX.Core/Search/Models/RagResponse.cs`
- Test: `tests/AgentX.Tests/Search/WebCitationDisplayTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Search/WebCitationDisplayTests.cs
using AgentX.Core.Search.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Search;

public class WebCitationDisplayTests
{
    [Fact]
    public void WebCitation_VaultSource_IsCorrect()
    {
        var citation = new WebCitation
        {
            Title = "Local Doc",
            Source = WebCitationSource.Vault,
            DocumentName = "research.pdf"
        };
        citation.Source.Should().Be(WebCitationSource.Vault);
        citation.DocumentName.Should().Be("research.pdf");
    }

    [Fact]
    public void WebCitation_WebSource_HasUrl()
    {
        var citation = new WebCitation
        {
            Title = "Web Result",
            Url = "https://example.com/article",
            Source = WebCitationSource.Web
        };
        citation.Url.Should().Be("https://example.com/article");
        citation.Source.Should().Be(WebCitationSource.Web);
    }
}
```

- [ ] **Step 2: Add web citation display in ChatPage.xaml**

After each assistant message, if `WebCitations` exist, show a citation list with [Vault] and [Web] tags:

```xml
<!-- Add after the assistant message content in the message template -->
<ItemsRepeater
    ItemsSource="{x:Bind WebCitations, Mode=OneWay}"
    Visibility="{x:Bind HasWebCitations, Mode=OneWay}">
    <ItemsRepeater.ItemTemplate>
        <DataTemplate>
            <Border
                Padding="8,4"
                Margin="0,2"
                CornerRadius="4"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Border
                        Padding="4,2"
                        CornerRadius="2"
                        Background="{ThemeResource AccentFillColorDefaultBrush}">
                        <TextBlock
                            FontSize="10"
                            FontWeight="SemiBold">
                            <Run Text="[Vault]" />
                        </TextBlock>
                    </Border>
                    <TextBlock Text="{Binding Title}" FontSize="12" />
                    <HyperlinkButton
                        NavigateUri="{Binding Url}"
                        FontSize="11"
                        Content="Open" />
                    <Button
                        Content="&#xE734;"
                        FontFamily="Segoe MDL2 Assets"
                        FontSize="10"
                        ToolTipService.ToolTip="Save to Vault"
                        Click="SaveToVault_Click"
                        Tag="{Binding}" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

Note: The implementer must bind `WebCitations` visibility based on whether the citation list is non-empty. Use a `Visibility` converter or a computed `HasWebCitations` property. The [Vault]/[Web] tag color should differ — [Vault] in accent color, [Web] in a different color (e.g., orange/amber).

- [ ] **Step 3: Add "Save to Vault" handler**

```csharp
// In ChatPage.xaml.cs
private async void SaveToVault_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement fe && fe.Tag is WebCitation citation)
    {
        var inboxService = App.Current.Services.GetRequiredService<IInboxService>();
        await inboxService.AddToInboxAsync(new InboxItem
        {
            Title = citation.Title,
            Content = citation.Snippet,
            SourceType = "WebClip",
            SourceUrl = citation.Url
        });

        _viewModel.ShowNotification($"Saved \"{citation.Title}\" to Smart Inbox");
    }
}
```

Note: The implementer should check the `IInboxService.AddToInboxAsync` signature and `InboxItem` constructor to ensure it matches. The `IInboxService` was added in v1.4 with `sourceType` and `sourceUrl` parameters.

- [ ] **Step 4: Add web citation properties to ChatMessageItem**

```csharp
// Add to ChatMessageItem inner class in ChatViewModel:
public IReadOnlyList<WebCitation>? WebCitations { get; set; }
public bool HasWebCitations => WebCitations?.Count > 0;
```

After receiving a RAG response with web citations, set the `WebCitations` on the corresponding assistant message item.

- [ ] **Step 5: Add [Web] and [Vault] tag rendering**

Create a value converter or use inline XAML to change the tag text and color based on `WebCitationSource`:

```xml
<!-- Use different colors for Vault vs Web citations -->
<Border
    Padding="4,2"
    CornerRadius="2">
    <Border.Background>
        <SolidColorBrush Color="{x:Bind Source, Mode=OneWay, Converter={StaticResource CitationSourceColorConverter}}" />
    </Border.Background>
    <TextBlock FontSize="10" FontWeight="SemiBold">
        <Run Text="{x:Bind Source, Mode=OneWay, Converter={StaticResource CitationSourceTextConverter}}" />
    </TextBlock>
</Border>
```

Create the converters:

```csharp
// src/AgentX.App/Converters/CitationSourceColorConverter.cs
using AgentX.Core.Search.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgentX.App.Converters;

public class CitationSourceColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is WebCitationSource source
            ? source == WebCitationSource.Vault
                ? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue)
                : new SolidColorBrush(Microsoft.UI.Colors.Orange)
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public class CitationSourceTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is WebCitationSource source
            ? source == WebCitationSource.Vault ? "Vault" : "Web"
            : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

- [ ] **Step 6: Add Research Mode indicator to assistant messages**

When `IsResearchMode` is true, prefix the assistant response display with a small badge:

```xml
<!-- Add at the top of assistant message template -->
<Border
    Background="{ThemeResource InfoBarInformationalSeverityBackgroundBrush}"
    CornerRadius="4"
    Padding="6,2"
    Margin="0,0,0,4"
    Visibility="{x:Bind ViewModel.IsResearchMode, Mode=OneWay}">
    <StackPanel Orientation="Horizontal" Spacing="4">
        <FontIcon Glyph="&#xE774;" FontSize="12" />
        <TextBlock Text="Research Mode" FontSize="11" FontStyle="Italic" />
    </StackPanel>
</Border>
```

- [ ] **Step 7: Run test to verify**

Run: `dotnet test tests/AgentX.Tests --filter "WebCitationDisplayTests" -v n -r win-x64`
Expected: PASS

- [ ] **Step 8: Verify build**

Run: `dotnet build src/AgentX.App -r win-x64`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/AgentX.App/ViewModels/ChatViewModel.cs src/AgentX.App/Views/ChatPage.xaml src/AgentX.App/Views/ChatPage.xaml.cs src/AgentX.App/Converters/CitationSourceColorConverter.cs src/AgentX.Core/Search/Models/RagResponse.cs src/AgentX.Core/Search/Models/WebCitation.cs tests/AgentX.Tests/Search/WebCitationDisplayTests.cs
git commit -m "feat(research): add web citation display, [Vault]/[Web] tags, Save to Vault action, and Research Mode indicator"
```