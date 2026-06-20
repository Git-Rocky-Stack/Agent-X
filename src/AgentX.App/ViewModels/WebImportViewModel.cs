using System.Collections.ObjectModel;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
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
        ICollectionService collectionService)
    {
        _webImportService = webImportService;
        _webScraperService = webScraperService;
        _collectionService = collectionService;
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
            StatusMessage = "Please enter a valid URL";
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
                StatusMessage = "Preview loaded";
            }
            else
            {
                StatusMessage = $"Failed to extract: {content.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to preview URL: {Url}", url);
            StatusMessage = $"Preview failed: {ex.Message}";
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
            StatusMessage = "No valid URLs found";
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

            var documents = await _webImportService.ImportFromUrlsAsync(
                urls, collectionId, progress, _importCts.Token);

            // Build results
            for (int i = 0; i < urls.Count; i++)
            {
                var doc = i < documents.Count ? documents[i] : null;
                var success = doc is not null;

                ImportResults.Add(new WebImportResultItem
                {
                    Url = urls[i],
                    DocumentName = doc?.FileName ?? "Failed",
                    Success = success,
                    WordCount = doc?.WordCount ?? 0,
                    ErrorMessage = success ? null : "Failed to import"
                });

                if (success) SuccessCount++;
                else FailCount++;
            }

            HasResults = true;
            StatusMessage = $"Imported {SuccessCount} of {urls.Count} URLs";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Import cancelled";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Batch URL import failed");
            StatusMessage = $"Import failed: {ex.Message}";
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
        FeedStatusMessage = "Subscribing...";

        try
        {
            var feedService = App.GetService<IFeedService>();
            var feed = await feedService.ParseFeedAsync(FeedUrl);
            FeedStatusMessage = $"Subscribed: {feed.Title} ({feed.Items.Count} items)";

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
                        StatusMessage = $"Importing {completed}/{urls.Count}...";
                    });

                    long? collectionId = SelectedCollection?.Id;
                    var documents = await _webImportService.ImportFromUrlsAsync(
                        urls, collectionId, progress, _importCts.Token);

                    for (int i = 0; i < urls.Count; i++)
                    {
                        var doc = i < documents.Count ? documents[i] : null;
                        var success = doc is not null;

                        ImportResults.Add(new WebImportResultItem
                        {
                            Url = urls[i],
                            DocumentName = doc?.FileName ?? "Failed",
                            Success = success,
                            WordCount = doc?.WordCount ?? 0,
                            ErrorMessage = success ? null : "Failed to import"
                        });

                        if (success) SuccessCount++;
                        else FailCount++;
                    }

                    HasResults = true;
                    StatusMessage = $"Imported {SuccessCount} of {urls.Count} feed items";
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Feed import cancelled";
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
            FeedStatusMessage = $"Error: {ex.Message}";
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
        StatusMessage = "Parsing sitemap...";

        try
        {
            var sitemapParser = App.GetService<ISitemapParser>();
            var urls = await sitemapParser.ParseSitemapAsync(SitemapUrl);
            StatusMessage = $"Found {urls.Count} URLs. Importing...";

            var urlsToImport = urls.Take(100).ToList();
            if (urlsToImport.Count == 0)
            {
                StatusMessage = "No URLs found in sitemap";
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
                    StatusMessage = $"Importing {completed}/{urlsToImport.Count}...";
                });

                long? collectionId = SelectedCollection?.Id;
                var documents = await _webImportService.ImportFromUrlsAsync(
                    urlsToImport, collectionId, progress, _importCts.Token);

                for (int i = 0; i < urlsToImport.Count; i++)
                {
                    var doc = i < documents.Count ? documents[i] : null;
                    var success = doc is not null;

                    ImportResults.Add(new WebImportResultItem
                    {
                        Url = urlsToImport[i],
                        DocumentName = doc?.FileName ?? "Failed",
                        Success = success,
                        WordCount = doc?.WordCount ?? 0,
                        ErrorMessage = success ? null : "Failed to import"
                    });

                    if (success) SuccessCount++;
                    else FailCount++;
                }

                HasResults = true;
                StatusMessage = $"Imported {SuccessCount} of {urlsToImport.Count} sitemap URLs";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Sitemap import cancelled";
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
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
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
