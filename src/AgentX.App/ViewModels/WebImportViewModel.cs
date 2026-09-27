using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Web;
using AgentX.Core.Services.Web.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class WebImportViewModel : ObservableObject
{
    // ── Services ─────────────────────────────────────────────
    private readonly IWebImportService _webImportService;
    private readonly IWebScraperService _webScraperService;
    private readonly ICollectionService _collectionService;
    private readonly ILocalizationService _localization;

    // ── Input State ──────────────────────────────────────────
    [ObservableProperty] private string _urlInput = string.Empty;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _importProgress;
    [ObservableProperty] private int _importTotal;

    // ── Preview State ────────────────────────────────────────
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewContent = string.Empty;
    [ObservableProperty] private string _previewAuthor = string.Empty;
    [ObservableProperty] private string _previewSiteName = string.Empty;
    [ObservableProperty] private long _previewWordCount;
    [ObservableProperty] private bool _hasPreview;

    // ── Collection Selection ─────────────────────────────────
    public ObservableCollection<CollectionEntity> Collections { get; } = new();
    [ObservableProperty] private CollectionEntity? _selectedCollection;

    // ── Results ──────────────────────────────────────────────
    public ObservableCollection<WebImportResultItem> ImportResults { get; } = new();
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failCount;

    // ── Feed & Sitemap State ────────────────────────────────
    [ObservableProperty] private string _feedUrl = string.Empty;
    [ObservableProperty] private string _sitemapUrl = string.Empty;
    [ObservableProperty] private bool _isSubscribingFeed;
    [ObservableProperty] private string _feedStatusMessage = string.Empty;

    private CancellationTokenSource? _importCts;

    public WebImportViewModel(
        IWebImportService webImportService,
        IWebScraperService webScraperService,
        ICollectionService collectionService,
        ILocalizationService localization)
    {
        _webImportService = webImportService;
        _webScraperService = webScraperService;
        _collectionService = collectionService;
        _localization = localization;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var collections = await _collectionService.GetAllCollectionsAsync();
            Collections.Clear();
            foreach (var c in collections)
            {
                Collections.Add(c);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load collections for web import");
        }
    }

    [RelayCommand]
    private async Task PreviewUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;

        var url = UrlInput.Trim().Split('\n').FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(url) || !_webScraperService.IsValidUrl(url))
        {
            StatusMessage = _localization.GetString("WebImport_EnterValidUrl");
            return;
        }

        IsPreviewing = true;
        HasPreview = false;

        try
        {
            var content = await _webScraperService.ExtractContentAsync(url, CancellationToken.None);

            if (content.Success)
            {
                PreviewTitle = content.Title;
                PreviewContent = content.Content.Length > 500
                    ? content.Content[..500] + "..."
                    : content.Content;
                PreviewAuthor = content.Author ?? string.Empty;
                PreviewSiteName = content.SiteName ?? string.Empty;
                PreviewWordCount = content.WordCount;
                HasPreview = true;
                StatusMessage = _localization.GetString("WebImport_PreviewLoaded");
            }
            else
            {
                StatusMessage = _localization.GetString("WebImport_ExtractFailed", content.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to preview URL: {Url}", url);
            StatusMessage = _localization.GetString("WebImport_PreviewFailed", ex.Message);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [RelayCommand]
    private async Task ImportUrlsAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;

        var urls = UrlInput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => _webScraperService.IsValidUrl(u))
            .ToList();

        if (urls.Count == 0)
        {
            StatusMessage = _localization.GetString("WebImport_NoValidUrls");
            return;
        }

        IsImporting = true;
        ImportProgress = 0;
        ImportTotal = urls.Count;
        ImportResults.Clear();
        SuccessCount = 0;
        FailCount = 0;
        _importCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<int>(completed =>
            {
                ImportProgress = completed;
            });

            long? collectionId = SelectedCollection?.Id;

            var results = await _webImportService.ImportFromUrlsAsync(
                urls, collectionId, progress, _importCts.Token);

            ShowImportResults(results);
            StatusMessage = urls.Count == 1
                ? _localization.GetString("WebImport_ImportedUrlsOne", SuccessCount, urls.Count)
                : _localization.GetString("WebImport_ImportedUrlsMany", SuccessCount, urls.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.GetString("WebImport_ImportCancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Batch URL import failed");
            StatusMessage = _localization.GetString("WebImport_ImportFailed", ex.Message);
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _importCts?.Cancel();
    }

    [RelayCommand]
    private void ClearResults()
    {
        ImportResults.Clear();
        HasResults = false;
        UrlInput = string.Empty;
        HasPreview = false;
        StatusMessage = string.Empty;
    }

    // ── Feed Subscription ────────────────────────────────────

    [RelayCommand]
    private async Task SubscribeToFeedAsync()
    {
        if (string.IsNullOrWhiteSpace(FeedUrl)) return;

        IsSubscribingFeed = true;
        FeedStatusMessage = _localization.GetString("WebImport_ReadingFeed");

        try
        {
            var feedService = App.GetService<IFeedService>();
            var feed = await feedService.ParseFeedAsync(FeedUrl);

            // No subscription is stored: this imports the items the feed lists right now, once.
            FeedStatusMessage = feed.Items.Count == 1
                ? _localization.GetString("WebImport_FeedReadOne", feed.Title, feed.Items.Count)
                : _localization.GetString("WebImport_FeedReadMany", feed.Title, feed.Items.Count);

            var urls = feed.Items
                .Select(i => i.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            if (urls.Count > 0)
            {
                IsImporting = true;
                ImportProgress = 0;
                ImportTotal = urls.Count;
                ImportResults.Clear();
                SuccessCount = 0;
                FailCount = 0;
                _importCts = new CancellationTokenSource();

                try
                {
                    var progress = new Progress<int>(completed =>
                    {
                        ImportProgress = completed;
                        StatusMessage = _localization.GetString("WebImport_ImportingProgress", completed, urls.Count);
                    });

                    long? collectionId = SelectedCollection?.Id;

                    // Feed item links are remote content, held to the feed's own network zone
                    var results = await _webImportService.ImportDiscoveredUrlsAsync(
                        FeedUrl, urls, collectionId, progress, _importCts.Token);

                    ShowImportResults(results);
                    StatusMessage = urls.Count == 1
                        ? _localization.GetString("WebImport_ImportedFeedItemsOne", SuccessCount, urls.Count)
                        : _localization.GetString("WebImport_ImportedFeedItemsMany", SuccessCount, urls.Count);
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = _localization.GetString("WebImport_FeedImportCancelled");
                }
                finally
                {
                    IsImporting = false;
                    _importCts?.Dispose();
                    _importCts = null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe to feed: {Url}", FeedUrl);
            FeedStatusMessage = _localization.GetString("WebImport_Error", ex.Message);
        }
        finally
        {
            IsSubscribingFeed = false;
        }
    }

    // ── Sitemap Import ──────────────────────────────────────

    [RelayCommand]
    private async Task ImportSitemapAsync()
    {
        if (string.IsNullOrWhiteSpace(SitemapUrl)) return;

        IsImporting = true;
        StatusMessage = _localization.GetString("WebImport_ParsingSitemap");

        try
        {
            var sitemapParser = App.GetService<ISitemapParser>();
            var urls = await sitemapParser.ParseSitemapAsync(SitemapUrl);
            StatusMessage = urls.Count == 1
                ? _localization.GetString("WebImport_SitemapFoundOne", urls.Count)
                : _localization.GetString("WebImport_SitemapFoundMany", urls.Count);

            var urlsToImport = urls.Take(100).ToList();
            if (urlsToImport.Count == 0)
            {
                StatusMessage = _localization.GetString("WebImport_SitemapNoUrls");
                return;
            }

            ImportProgress = 0;
            ImportTotal = urlsToImport.Count;
            ImportResults.Clear();
            SuccessCount = 0;
            FailCount = 0;
            _importCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<int>(completed =>
                {
                    ImportProgress = completed;
                    StatusMessage = _localization.GetString("WebImport_ImportingProgress", completed, urlsToImport.Count);
                });

                long? collectionId = SelectedCollection?.Id;

                // Sitemap entries are remote content, held to the sitemap's own network zone
                var results = await _webImportService.ImportDiscoveredUrlsAsync(
                    SitemapUrl, urlsToImport, collectionId, progress, _importCts.Token);

                ShowImportResults(results);
                StatusMessage = urlsToImport.Count == 1
                    ? _localization.GetString("WebImport_ImportedSitemapUrlsOne", SuccessCount, urlsToImport.Count)
                    : _localization.GetString("WebImport_ImportedSitemapUrlsMany", SuccessCount, urlsToImport.Count);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = _localization.GetString("WebImport_SitemapImportCancelled");
            }
            finally
            {
                _importCts?.Dispose();
                _importCts = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import sitemap: {Url}", SitemapUrl);
            StatusMessage = _localization.GetString("WebImport_Error", ex.Message);
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Lists one row per imported URL. Each result carries its own URL, so a failure never
    /// shifts a document onto the wrong URL; a failed row shows why it failed.
    /// </summary>
    private void ShowImportResults(IReadOnlyList<WebImportResult> results)
    {
        foreach (var result in results)
        {
            ImportResults.Add(new WebImportResultItem
            {
                Url = result.Url,
                DocumentName = result.Document?.FileName
                    ?? _localization.GetString("WebImport_RowFailed", result.ErrorMessage ?? string.Empty),
                Success = result.Success,
                WordCount = result.Document?.WordCount ?? 0,
                ErrorMessage = result.ErrorMessage
            });

            if (result.Success) SuccessCount++;
            else FailCount++;
        }

        HasResults = true;
    }
}

public partial class WebImportResultItem : ObservableObject
{
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _documentName = string.Empty;
    [ObservableProperty] private bool _success;
    [ObservableProperty] private long _wordCount;
    [ObservableProperty] private string? _errorMessage;
}
