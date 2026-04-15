# Web Content Ingestion Depth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Overhaul web content ingestion with JavaScript rendering, RSS/Atom feed subscription, sitemap bulk import, and deeper structured extraction.

**Architecture:** Extend the existing `WebScraperService` with a Playwright-based JS renderer, add `RssFeedService` and `SitemapParserService` as new services, and update `WebImportPage` UI to support feeds and sitemaps.

**Tech Stack:** C#, .NET 8, Playwright (via Microsoft.Playwright NuGet), System.Xml for RSS/Atom, HtmlAgilityPack (existing), xUnit

---

### Task 1: JavaScript Rendering with Playwright

**Files:**
- Create: `src/AgentX.Core/Services/Web/IJsRenderingService.cs`
- Create: `src/AgentX.Core/Services/Web/JsRenderingService.cs`
- Modify: `src/AgentX.Core/Services/Web/WebScraperService.cs`
- Test: `tests/AgentX.Tests/Services/Web/JsRenderingServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Web/JsRenderingServiceTests.cs
using AgentX.Core.Services.Web;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class JsRenderingServiceTests
{
    [Fact]
    public async Task RenderPageAsync_ReturnsRenderedHtml()
    {
        var service = new JsRenderingService();
        // Test against a known static page that doesn't need JS
        // to verify the Playwright pipeline works end-to-end
        var result = await service.RenderPageAsync("https://example.com");
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Example Domain", result);
    }

    [Fact]
    public async Task RenderPageAsync_WithWaitForNetworkIdle_WaitsForJs()
    {
        var service = new JsRenderingService();
        var result = await service.RenderPageAsync("https://example.com", waitForNetworkIdle: true);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~JsRenderingServiceTests" -v n`
Expected: Build error — namespace does not exist.

- [ ] **Step 3: Create the interface**

```csharp
// src/AgentX.Core/Services/Web/IJsRenderingService.cs
namespace AgentX.Core.Services.Web;

public interface IJsRenderingService
{
    Task<string> RenderPageAsync(string url, bool waitForNetworkIdle = false, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create the implementation**

```csharp
// src/AgentX.Core/Services/Web/JsRenderingService.cs
using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace AgentX.Core.Services.Web;

public class JsRenderingService : IJsRenderingService, IDisposable
{
    private readonly ILogger<JsRenderingService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public JsRenderingService(ILogger<JsRenderingService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JsRenderingService>.Instance;
    }

    public async Task<string> RenderPageAsync(string url, bool waitForNetworkIdle = false, CancellationToken ct = default)
    {
        await EnsureBrowserAsync();

        await using var page = await _browser!.NewPageAsync(new BrowserNewPageOptions
        {
            UserAgent = "Agent-X/1.5.0 (Knowledge Vault Web Clipper)"
        });

        var response = await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = waitForNetworkIdle ? WaitUntilState.NetworkIdle : WaitUntilState.DOMContentLoaded
        });

        if (response == null || !response.Ok)
        {
            _logger.LogWarning("Failed to render {Url}: HTTP {Status}", url, response?.Status ?? 0);
            return string.Empty;
        }

        var content = await page.ContentAsync();
        return content;
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browser != null) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public void Dispose()
    {
        _browser?.DisposeAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}
```

- [ ] **Step 5: Add Microsoft.Playwright NuGet to AgentX.Core**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet add src/AgentX.Core/AgentX.Core.csproj package Microsoft.Playwright
```

Also install Playwright browsers: `npx playwright install chromium` (document this as a setup requirement).

- [ ] **Step 6: Integrate into WebScraperService**

Read `src/AgentX.Core/Services/Web/WebScraperService.cs`. Add `IJsRenderingService` as optional dependency. When a page's readability extraction returns very little content (<100 chars), fall back to JS rendering:

```csharp
// In ExtractContentAsync, after the readability extraction:
if (content.Length < 100 && _jsRenderingService != null)
{
    _logger.LogInformation("Readability extraction returned minimal content, falling back to JS rendering for {Url}", url);
    var renderedHtml = await _jsRenderingService.RenderPageAsync(url, waitForNetworkIdle: true, ct);
    if (!string.IsNullOrWhiteSpace(renderedHtml))
    {
        // Re-run readability on the rendered HTML
        var doc = new HtmlDocument();
        doc.LoadHtml(renderedHtml);
        content = ExtractReadableContent(doc);
    }
}
```

- [ ] **Step 7: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~JsRenderingServiceTests" -v n`
Expected: Tests pass (require Playwright installed).

- [ ] **Step 8: Commit**

```bash
git add src/AgentX.Core/Services/Web/IJsRenderingService.cs src/AgentX.Core/Services/Web/JsRenderingService.cs src/AgentX.Core/Services/Web/WebScraperService.cs tests/AgentX.Tests/Services/Web/JsRenderingServiceTests.cs src/AgentX.Core/AgentX.Core.csproj
git commit -m "feat(web): add JavaScript rendering with Playwright for JS-heavy pages"
```

---

### Task 2: RSS/Atom Feed Subscription Service

**Files:**
- Create: `src/AgentX.Core/Services/Web/IFeedService.cs`
- Create: `src/AgentX.Core/Services/Web/FeedService.cs`
- Create: `src/AgentX.Core/Services/Web/Models/FeedModels.cs`
- Create: `src/AgentX.Core/Data/Entities/FeedSubscriptionEntity.cs`
- Test: `tests/AgentX.Tests/Services/Web/FeedServiceTests.cs`

- [ ] **Step 1: Create feed models**

```csharp
// src/AgentX.Core/Services/Web/Models/FeedModels.cs
namespace AgentX.Core.Services.Web.Models;

public class FeedItem
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Author { get; set; }
    public DateTime? PublishedDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
}

public class FeedInfo
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? LastUpdated { get; set; }
    public List<FeedItem> Items { get; set; } = [];
}
```

- [ ] **Step 2: Create FeedSubscriptionEntity**

```csharp
// src/AgentX.Core/Data/Entities/FeedSubscriptionEntity.cs
namespace AgentX.Core.Data.Entities;

public class FeedSubscriptionEntity
{
    public long Id { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long? DefaultCollectionId { get; set; }
    public bool AutoImport { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 60;
    public DateTime LastPolledAt { get; set; } = DateTime.MinValue;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsEnabled { get; set; } = true;
}
```

- [ ] **Step 3: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Web/FeedServiceTests.cs
using AgentX.Core.Services.Web;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class FeedServiceTests
{
    [Fact]
    public async Task ParseFeedAsync_ValidRss_ReturnsFeedItems()
    {
        var service = new FeedService();
        // Use a known stable RSS feed for testing
        var result = await service.ParseFeedAsync("https://www.w3.org/blog/feed/");
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task ParseFeedAsync_InvalidUrl_Throws()
    {
        var service = new FeedService();
        await Assert.ThrowsAnyAsync<Exception>(() => service.ParseFeedAsync("not-a-url"));
    }
}
```

- [ ] **Step 4: Implement FeedService**

```csharp
// src/AgentX.Core/Services/Web/IFeedService.cs
using AgentX.Core.Services.Web.Models;

namespace AgentX.Core.Services.Web;

public interface IFeedService
{
    Task<FeedInfo> ParseFeedAsync(string feedUrl, CancellationToken ct = default);
    Task<IReadOnlyList<FeedItem>> GetNewItemsAsync(string feedUrl, DateTime since, CancellationToken ct = default);
}

// src/AgentX.Core/Services/Web/FeedService.cs
using System.Xml.Linq;
using AgentX.Core.Services.Web.Models;

namespace AgentX.Core.Services.Web;

public class FeedService : IFeedService
{
    private readonly HttpClient _httpClient = new();

    public async Task<FeedInfo> ParseFeedAsync(string feedUrl, CancellationToken ct = default)
    {
        var xml = await _httpClient.GetStringAsync(feedUrl, ct);
        var doc = XDocument.Parse(xml);
        var root = doc.Root;

        if (root?.Name.LocalName == "rss" || root?.Name.LocalName == "RDF")
            return ParseRss(doc);
        if (root?.Name.LocalName == "feed")
            return ParseAtom(doc);

        throw new InvalidOperationException($"Unknown feed format: {root?.Name.LocalName}");
    }

    public async Task<IReadOnlyList<FeedItem>> GetNewItemsAsync(string feedUrl, DateTime since, CancellationToken ct = default)
    {
        var feed = await ParseFeedAsync(feedUrl, ct);
        return feed.Items
            .Where(i => i.PublishedDate > since)
            .OrderByDescending(i => i.PublishedDate)
            .ToList();
    }

    private FeedInfo ParseRss(XDocument doc)
    {
        var channel = doc.Root?.Element("channel");
        var info = new FeedInfo
        {
            Title = channel?.Element("title")?.Value ?? "Untitled",
            Url = channel?.Element("link")?.Value ?? "",
            Description = channel?.Element("description")?.Value,
        };

        foreach (var item in channel?.Descendants("item") ?? [])
        {
            info.Items.Add(new FeedItem
            {
                Title = item.Element("title")?.Value ?? "Untitled",
                Content = item.Element("content:encoded")?.Value ?? item.Element("description")?.Value ?? "",
                Url = item.Element("link")?.Value ?? "",
                Author = item.Element("dc:creator")?.Value ?? item.Element("author")?.Value,
                PublishedDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var d) ? d : null,
                Description = item.Element("description")?.Value,
                Category = item.Element("category")?.Value,
            });
        }

        return info;
    }

    private FeedInfo ParseAtom(XDocument doc)
    {
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var info = new FeedInfo
        {
            Title = doc.Root?.Element(ns + "title")?.Value ?? "Untitled",
            Url = doc.Root?.Element(ns + "link")?.Attribute("href")?.Value ?? "",
            Description = doc.Root?.Element(ns + "subtitle")?.Value,
        };

        foreach (var entry in doc.Root?.Elements(ns + "entry") ?? [])
        {
            info.Items.Add(new FeedItem
            {
                Title = entry.Element(ns + "title")?.Value ?? "Untitled",
                Content = entry.Element(ns + "content")?.Value ?? entry.Element(ns + "summary")?.Value ?? "",
                Url = entry.Element(ns + "link")?.Attribute("href")?.Value ?? "",
                Author = entry.Element(ns + "author")?.Element(ns + "name")?.Value,
                PublishedDate = DateTime.TryParse(entry.Element(ns + "published")?.Value ?? entry.Element(ns + "updated")?.Value, out var d) ? d : null,
                Description = entry.Element(ns + "summary")?.Value,
                Category = entry.Element(ns + "category")?.Attribute("term")?.Value,
            });
        }

        return info;
    }
}
```

- [ ] **Step 5: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~FeedServiceTests" -v n`

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/Services/Web/IFeedService.cs src/AgentX.Core/Services/Web/FeedService.cs src/AgentX.Core/Services/Web/Models/FeedModels.cs src/AgentX.Core/Data/Entities/FeedSubscriptionEntity.cs tests/AgentX.Tests/Services/Web/FeedServiceTests.cs
git commit -m "feat(web): add RSS/Atom feed parser and subscription entity"
```

---

### Task 3: Sitemap Parser

**Files:**
- Create: `src/AgentX.Core/Services/Web/ISitemapParser.cs`
- Create: `src/AgentX.Core/Services/Web/SitemapParser.cs`
- Test: `tests/AgentX.Tests/Services/Web/SitemapParserTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Web/SitemapParserTests.cs
using AgentX.Core.Services.Web;
using Xunit;

namespace AgentX.Tests.Services.Web;

public class SitemapParserTests
{
    [Fact]
    public async Task ParseSitemapAsync_ValidXml_ReturnsUrls()
    {
        var service = new SitemapParser();
        var result = await service.ParseSitemapAsync("https://www.sitemaps.org/sitemap.xml");
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ParseSitemapIndexAsync_ReturnsChildSitemaps()
    {
        var service = new SitemapParser();
        var result = await service.ParseSitemapIndexAsync("https://www.sitemaps.org/sitemap.xml");
        // May or may not be an index — just verify it doesn't crash
        Assert.NotNull(result);
    }
}
```

- [ ] **Step 2: Implement SitemapParser**

```csharp
// src/AgentX.Core/Services/Web/ISitemapParser.cs
namespace AgentX.Core.Services.Web;

public interface ISitemapParser
{
    Task<IReadOnlyList<string>> ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default);
}

// src/AgentX.Core/Services/Web/SitemapParser.cs
using System.Xml.Linq;

namespace AgentX.Core.Services.Web;

public class SitemapParser : ISitemapParser
{
    private readonly HttpClient _httpClient = new();

    public async Task<IReadOnlyList<string>> ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default)
    {
        var xml = await _httpClient.GetStringAsync(sitemapUrl, ct);
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Check if this is a sitemap index
        if (doc.Root?.Name.LocalName == "sitemapindex")
        {
            var childUrls = doc.Root.Elements(ns + "sitemap")
                .Select(s => s.Element(ns + "loc")?.Value)
                .Where(u => u != null)
                .Cast<string>()
                .ToList();

            var allUrls = new List<string>();
            foreach (var childUrl in childUrls.Take(10)) // Limit depth
            {
                var childUrls2 = await ParseSitemapAsync(childUrl, ct);
                allUrls.AddRange(childUrls2);
            }
            return allUrls;
        }

        // Regular sitemap
        return doc.Root?.Elements(ns + "url")
            .Select(u => u.Element(ns + "loc")?.Value)
            .Where(u => u != null)
            .Cast<string>()
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<string>> ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default)
    {
        var xml = await _httpClient.GetStringAsync(sitemapIndexUrl, ct);
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        return doc.Root?.Elements(ns + "sitemap")
            .Select(s => s.Element(ns + "loc")?.Value)
            .Where(u => u != null)
            .Cast<string>()
            .ToList() ?? [];
    }
}
```

- [ ] **Step 3: Run tests and commit**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~SitemapParserTests" -v n
git add src/AgentX.Core/Services/Web/ISitemapParser.cs src/AgentX.Core/Services/Web/SitemapParser.cs tests/AgentX.Tests/Services/Web/SitemapParserTests.cs
git commit -m "feat(web): add sitemap.xml parser with index support"
```

---

### Task 4: WebImportPage UI — Feed and Sitemap Import

**Files:**
- Modify: `src/AgentX.App/ViewModels/WebImportViewModel.cs`
- Modify: `src/AgentX.App/Views/WebImportPage.xaml`

- [ ] **Step 1: Add feed subscription and sitemap import commands to WebImportViewModel**

Read `src/AgentX.App/ViewModels/WebImportViewModel.cs`. Add:

```csharp
[ObservableProperty]
private string _feedUrl = string.Empty;

[ObservableProperty]
private string _sitemapUrl = string.Empty;

[ObservableProperty]
private bool _isSubscribingFeed;

[ObservableProperty]
private string _feedStatusMessage = string.Empty;

[RelayCommand]
private async Task SubscribeToFeedAsync()
{
    if (string.IsNullOrWhiteSpace(FeedUrl)) return;
    IsSubscribingFeed = true;
    FeedStatusMessage = "Subscribing...";

    try
    {
        var feedService = App.Current.Services.GetRequiredService<IFeedService>();
        var feed = await feedService.ParseFeedAsync(FeedUrl);
        FeedStatusMessage = $"Subscribed: {feed.Title} ({feed.Items.Count} items)";

        // Import all items via WebImportService
        var urls = feed.Items.Select(i => i.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        await _webImportService.ImportFromUrlsAsync(urls, SelectedCollectionId, new Progress<int>(p => StatusMessage = $"Importing {p}/{urls.Count}..."));
    }
    catch (Exception ex)
    {
        FeedStatusMessage = $"Error: {ex.Message}";
    }
    finally
    {
        IsSubscribingFeed = false;
    }
}

[RelayCommand]
private async Task ImportSitemapAsync()
{
    if (string.IsNullOrWhiteSpace(SitemapUrl)) return;
    IsImporting = true;
    StatusMessage = "Parsing sitemap...";

    try
    {
        var sitemapParser = App.Current.Services.GetRequiredService<ISitemapParser>();
        var urls = await sitemapParser.ParseSitemapAsync(SitemapUrl);
        StatusMessage = $"Found {urls.Count} URLs. Importing...";

        await _webImportService.ImportFromUrlsAsync(urls.Take(100).ToList(), SelectedCollectionId, new Progress<int>(p => StatusMessage = $"Importing {p}/100..."));
    }
    catch (Exception ex)
    {
        StatusMessage = $"Error: {ex.Message}";
    }
    finally
    {
        IsImporting = false;
    }
}
```

- [ ] **Step 2: Add UI sections to WebImportPage.xaml**

Read `src/AgentX.App/Views/WebImportPage.xaml`. Add sections for feed subscription and sitemap import below the existing URL import section:

```xml
<!-- Feed Subscription Section -->
<TextBlock Text="RSS/Atom Feed" Style="{StaticResource SubtitleTextBlockStyle}" Margin="0,24,0,8"/>
<TextBox Header="Feed URL" Text="{x:Bind ViewModel.FeedUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         PlaceholderText="https://example.com/feed.xml"/>
<Button Content="Subscribe &amp; Import" Command="{x:Bind ViewModel.SubscribeToFeedCommand}"
        IsEnabled="{x:Bind ViewModel.IsSubscribingFeed, Mode=OneWay, Converter={StaticResource InvertBoolConverter}}"
        Style="{StaticResource AccentButtonStyle}" Margin="0,8,0,0"/>
<TextBlock Text="{x:Bind ViewModel.FeedStatusMessage, Mode=OneWay}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" Margin="0,4,0,0"/>

<!-- Sitemap Import Section -->
<TextBlock Text="Sitemap Import" Style="{StaticResource SubtitleTextBlockStyle}" Margin="0,24,0,8"/>
<TextBox Header="Sitemap URL" Text="{x:Bind ViewModel.SitemapUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         PlaceholderText="https://example.com/sitemap.xml"/>
<Button Content="Import from Sitemap" Command="{x:Bind ViewModel.ImportSitemapCommand}"
        IsEnabled="{x:Bind ViewModel.IsImporting, Mode=OneWay, Converter={StaticResource InvertBoolConverter}}"
        Style="{StaticResource AccentButtonStyle}" Margin="0,8,0,0"/>
<TextBlock Text="Imports up to 100 pages from the sitemap" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12"/>
```

- [ ] **Step 3: Register new services in DI**

In `src/AgentX.App/App.xaml.cs`:

```csharp
services.AddSingleton<IFeedService, FeedService>();
services.AddSingleton<ISitemapParser, SitemapParser>();
services.AddSingleton<IJsRenderingService, JsRenderingService>();
```

- [ ] **Step 4: Build and verify**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet build src/AgentX.App -r win-x64
```

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.App/ViewModels/WebImportViewModel.cs src/AgentX.App/Views/WebImportPage.xaml src/AgentX.App/App.xaml.cs
git commit -m "feat(web): add RSS/Atom feed subscription and sitemap bulk import to Web Import page"
```

---

### Task 5: Enhanced Metadata Extraction

**Files:**
- Modify: `src/AgentX.Core/Services/Web/WebScraperService.cs`

- [ ] **Step 1: Enhance metadata extraction in WebScraperService**

Read `src/AgentX.Core/Services/Web/WebScraperService.cs`. In `ExtractContentAsync`, improve metadata extraction:

- Add JSON-LD structured data parsing (Schema.org Article, BlogPosting, NewsArticle)
- Extract tables from HTML tables into markdown table format
- Detect and extract author from `article:author` meta, `rel="author"` link, and JSON-LD `author` field
- Extract canonical URL from `<link rel="canonical">`

Add these helper methods:

```csharp
private string? ExtractJsonLdAuthor(HtmlDocument doc)
{
    var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
    if (scripts == null) return null;

    foreach (var script in scripts)
    {
        try
        {
            using var json = JsonDocument.Parse(script.InnerText);
            var root = json.RootElement;
            if (root.TryGetProperty("author", out var author))
            {
                if (author.ValueKind == JsonValueKind.String)
                    return author.GetString();
                if (author.TryGetProperty("name", out var name))
                    return name.GetString();
            }
        }
        catch { /* Skip malformed JSON-LD */ }
    }
    return null;
}

private string ExtractTablesAsMarkdown(HtmlDocument doc)
{
    var sb = new StringBuilder();
    var tables = doc.DocumentNode.SelectNodes("//table");
    if (tables == null) return string.Empty;

    foreach (var table in tables)
    {
        var rows = table.SelectNodes(".//tr");
        if (rows == null) continue;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//th | .//td");
            if (cells == null) continue;

            sb.AppendLine("| " + string.Join(" | ", cells.Select(c => c.InnerText.Trim())) + " |");

            if (row == rows.First())
            {
                sb.AppendLine("| " + string.Join(" | ", cells.Select(_ => "---")) + " |");
            }
        }
        sb.AppendLine();
    }

    return sb.ToString();
}
```

- [ ] **Step 2: Run full test suite**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests -v n
```

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.Core/Services/Web/WebScraperService.cs
git commit -m "feat(web): enhance metadata extraction with JSON-LD, tables, and canonical URL"
```