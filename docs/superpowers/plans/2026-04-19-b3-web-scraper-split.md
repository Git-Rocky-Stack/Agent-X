# B3: WebScraperService Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `WebScraperService.cs` (1,545 LOC) into a pipeline of focused services: Fetcher, Parser, Structured Data Extractor. WebScraperService becomes a thin orchestrator ≤400 LOC.

**Architecture:** Pipeline pattern — WebContentFetcher handles HTTP + JS rendering, HtmlParser handles readability + text extraction, StructuredDataExtractor handles JSON-LD/OpenGraph/meta. WebScraperService composes the pipeline.

**Tech Stack:** C#, .NET 8, HtmlAgilityPack (existing), System.Text.Json, xUnit

---

### Task 1: WebContentFetcher + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Web/IWebContentFetcher.cs`
- Create: `src/AgentX.Core/Services/Web/WebContentFetcher.cs`
- Create: `tests/AgentX.Tests/Services/Web/WebContentFetcherTests.cs`

- [ ] **Step 1: Define IWebContentFetcher interface**

```csharp
public interface IWebContentFetcher
{
    Task<FetchResult> FetchAsync(string url, CancellationToken ct = default);
}

public record FetchResult(string Html, string? FinalUrl, TimeSpan Elapsed, bool UsedJsRendering);
```

- [ ] **Step 2: Write failing tests**

Tests: FetchAsync returns HTML for valid URL, follows redirects, sets correct User-Agent, falls back to JS rendering when configured, handles HTTP errors, respects cancellation, validates URL.

- [ ] **Step 3: Extract fetch logic from WebScraperService (lines 536-575)**

Move: HttpClient setup, User-Agent rotation, rate limiting, JS rendering fallback via IJsRenderingService, timeout handling, cookie handling.

- [ ] **Step 4: Run tests**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~WebContentFetcher" --blame-hang-timeout 60s
```

---

### Task 2: HtmlParser + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Web/IHtmlParser.cs`
- Create: `src/AgentX.Core/Services/Web/HtmlParser.cs`
- Create: `tests/AgentX.Tests/Services/Web/HtmlParserTests.cs`

- [ ] **Step 1: Define IHtmlParser interface**

```csharp
public interface IHtmlParser
{
    ParsedContent Parse(string html, string url);
    string ExtractReadabilityText(string html);
    Metadata ExtractMetadata(string html, string url);
}

public record ParsedContent(string Title, string Text, string? Description, string? Author, DateTime? PublishedDate, TimeSpan? ReadingTime);
public record Metadata(string? Title, string? Description, string? Author, string?ImageUrl, DateTime? PublishedDate, string? SiteName);
```

- [ ] **Step 2: Write failing tests**

Tests: Parse extracts title and text from article HTML, handles non-article pages gracefully, ExtractReadabilityText removes nav/footer/script tags, ExtractMetadata pulls OpenGraph/meta tags, handles malformed HTML, calculates reading time correctly, handles Unicode content.

- [ ] **Step 3: Extract parsing logic from WebScraperService (lines 576-701, 904-1241)**

Move: Readability algorithm, metadata extraction, HTML cleanup, text normalization, reading time calculation, content block detection.

- [ ] **Step 4: Run tests**

---

### Task 3: StructuredDataExtractor + Tests

**Files:**
- Create: `src/AgentX.Core/Services/Web/IStructuredDataExtractor.cs`
- Create: `src/AgentX.Core/Services/Web/StructuredDataExtractor.cs`
- Create: `tests/AgentX.Tests/Services/Web/StructuredDataExtractorTests.cs`

- [ ] **Step 1: Define IStructuredDataExtractor interface**

```csharp
public interface IStructuredDataExtractor
{
    JsonLdData? ExtractJsonLd(string html);
    OpenGraphData? ExtractOpenGraph(string html);
    IReadOnlyList<StructuredTag> ExtractMetaTags(string html);
    string? ExtractAuthor(string html);
}

public record JsonLdData(string? Type, string? Name, string? Author, string? Description, DateTime? DatePublished);
public record OpenGraphData(string? Title, string? Description, string? Image, string? Url, string? Type);
```

- [ ] **Step 2: Write failing tests**

Tests: ExtractJsonLd parses Article schema, handles multiple JSON-LD blocks, ExtractOpenGraph pulls og:title/description/image, ExtractAuthor falls back through JSON-LD → meta → byline, handles missing structured data gracefully.

- [ ] **Step 3: Extract JSON-LD + structured data logic from WebScraperService (lines 707-774)**

Move: JSON-LD script tag extraction, OpenGraph parsing, meta tag extraction, author resolution chain.

- [ ] **Step 4: Run tests**

---

### Task 4: Thin Orchestrator + Integration Tests

**Files:**
- Modify: `src/AgentX.Core/Services/Web/WebScraperService.cs` (thin to ≤400 LOC)
- Create: `tests/AgentX.Tests/Services/Web/WebScraperServiceIntegrationTests.cs`

- [ ] **Step 1: Refactor WebScraperService to compose pipeline**

```csharp
public class WebScraperService : IWebScraperService
{
    private readonly IWebContentFetcher _fetcher;
    private readonly IHtmlParser _parser;
    private readonly IStructuredDataExtractor _extractor;
    private readonly ILogger _logger;

    public async Task<ScrapedContent> ExtractContentAsync(string url, CancellationToken ct)
    {
        var fetchResult = await _fetcher.FetchAsync(url, ct);
        var parsed = _parser.Parse(fetchResult.Html, url);
        var structured = _extractor.ExtractJsonLd(fetchResult.Html);
        // Merge and return
    }
}
```

- [ ] **Step 2: Write integration tests**

Tests: End-to-end scrape via pipeline, YouTube transcript extraction delegates correctly, batch processing works with progress reporting, error handling propagates through pipeline.

- [ ] **Step 3: Update DI registration**

- [ ] **Step 4: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

WebScraperService.cs ≤ 400 LOC. All new service tests + integration tests pass.

## Commit Strategy

- `refactor(web): WebContentFetcher extracted from WebScraperService`
- `refactor(web): HtmlParser with readability extraction`
- `refactor(web): StructuredDataExtractor for JSON-LD and OpenGraph`
- `refactor(web): thin WebScraperService pipeline orchestrator`
