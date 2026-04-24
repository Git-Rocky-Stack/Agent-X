using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.App.Services;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Collections;
using AgentX.Core.Data.Entities;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly IInboxService _inboxService;
    private readonly ICollectionService _collectionService;

    // ── Page State ───────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _statusFilter = "pending";

    // ── Inbox Items ──────────────────────────────────────────
    public ObservableCollection<InboxDisplayItem> InboxItems { get; } = new();
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private long _focusedInboxItemId;

    // ── Collection Selection ─────────────────────────────────
    public ObservableCollection<CollectionEntity> Collections { get; } = new();
    [ObservableProperty] private CollectionEntity? _selectedCollection;

    // ── Filter Options ───────────────────────────────────────
    public List<string> StatusFilters { get; } = new() { "pending", "accepted", "rejected", "deferred", "all" };

    private CancellationTokenSource? _previewCts;

    public InboxViewModel(
        IInboxService inboxService,
        ICollectionService collectionService,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _inboxService = inboxService;
        _collectionService = collectionService;
        _operationsDrillInService = operationsDrillInService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await LoadCollectionsAsync();
            await LoadInboxItemsAsync();
            PendingCount = await _inboxService.GetPendingCountAsync();
            await ApplyPendingOperationsRequestAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize InboxViewModel");
            StatusMessage = "Failed to load inbox";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCollectionsAsync()
    {
        try
        {
            var collections = await _collectionService.GetAllCollectionsAsync();
            Collections.Clear();
            foreach (var c in collections)
                Collections.Add(c);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load collections");
        }
    }

    private async Task LoadInboxItemsAsync()
    {
        try
        {
            FocusedInboxItemId = 0;
            var filter = StatusFilter == "all" ? null : StatusFilter;
            var items = await _inboxService.GetAllItemsAsync(filter, 0, 100);

            InboxItems.Clear();
            foreach (var item in items)
            {
                InboxItems.Add(new InboxDisplayItem
                {
                    Id = item.Id,
                    FileName = item.FileName,
                    FilePath = item.FilePath,
                    FileType = item.FileType,
                    FileSizeBytes = item.FileSizeBytes,
                    Status = item.Status,
                    Preview = item.Preview ?? string.Empty,
                    SuggestedCollectionName = item.SuggestedCollectionName ?? string.Empty,
                    SuggestedTags = item.SuggestedTags ?? string.Empty,
                    AddedAt = item.AddedAt,
                    HasPreview = !string.IsNullOrEmpty(item.Preview),
                    SourceType = item.SourceType ?? string.Empty,
                    SourceUrl = item.SourceUrl ?? string.Empty,
                    IsBrowserClip = item.SourceType == "browser-extension",
                    IsFocused = false
                });
            }

            HasItems = InboxItems.Count > 0;
            PendingCount = await _inboxService.GetPendingCountAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load inbox items");
        }
    }

    private async Task ApplyPendingOperationsRequestAsync()
    {
        var request = _operationsDrillInService?.ConsumePendingInboxRequest();
        if (request is null)
        {
            return;
        }

        var focusedItem = InboxItems.FirstOrDefault(item => item.Id == request.ItemId);
        if (focusedItem is null && StatusFilter != "all")
        {
            StatusFilter = "all";
            await LoadInboxItemsAsync();
            focusedItem = InboxItems.FirstOrDefault(item => item.Id == request.ItemId);
        }

        if (focusedItem is null)
        {
            StatusMessage = "The requested inbox item is no longer available.";
            return;
        }

        FocusedInboxItemId = request.ItemId;
        foreach (var item in InboxItems)
        {
            item.IsFocused = item.Id == request.ItemId;
        }

        var currentIndex = InboxItems.IndexOf(focusedItem);
        if (currentIndex > 0)
        {
            InboxItems.Move(currentIndex, 0);
        }

        StatusMessage = request.SourceLabel;
    }

    [RelayCommand]
    private async Task FilterByStatusAsync(string status)
    {
        StatusFilter = status;
        await LoadInboxItemsAsync();
    }

    [RelayCommand]
    private async Task AcceptItemAsync(long itemId)
    {
        try
        {
            await _inboxService.AcceptItemAsync(itemId, SelectedCollection?.Id);
            await LoadInboxItemsAsync();
            StatusMessage = "Item accepted and queued for indexing";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to accept inbox item {Id}", itemId);
            StatusMessage = "Failed to accept item";
        }
    }

    [RelayCommand]
    private async Task RejectItemAsync(long itemId)
    {
        try
        {
            await _inboxService.RejectItemAsync(itemId);
            await LoadInboxItemsAsync();
            StatusMessage = "Item rejected";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reject inbox item {Id}", itemId);
            StatusMessage = "Failed to reject item";
        }
    }

    [RelayCommand]
    private async Task DeferItemAsync(long itemId)
    {
        try
        {
            await _inboxService.DeferItemAsync(itemId);
            await LoadInboxItemsAsync();
            StatusMessage = "Item deferred";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to defer inbox item {Id}", itemId);
            StatusMessage = "Failed to defer item";
        }
    }

    [RelayCommand]
    private async Task AcceptAllAsync()
    {
        IsProcessing = true;
        try
        {
            await _inboxService.AcceptAllPendingAsync();
            await LoadInboxItemsAsync();
            StatusMessage = "All pending items accepted";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to accept all items");
            StatusMessage = "Failed to accept all";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task GeneratePreviewsAsync()
    {
        IsProcessing = true;
        _previewCts = new CancellationTokenSource();

        try
        {
            await _inboxService.GenerateAllPreviewsAsync(_previewCts.Token);
            await LoadInboxItemsAsync();
            StatusMessage = "AI previews generated";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Preview generation cancelled";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate previews");
            StatusMessage = "Failed to generate previews";
        }
        finally
        {
            IsProcessing = false;
            _previewCts?.Dispose();
            _previewCts = null;
        }
    }

    [RelayCommand]
    private async Task CleanupProcessedAsync()
    {
        try
        {
            await _inboxService.DeleteProcessedItemsAsync();
            await LoadInboxItemsAsync();
            StatusMessage = "Processed items cleaned up";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to cleanup processed items");
            StatusMessage = "Cleanup failed";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadInboxItemsAsync();
    }
}

public partial class InboxDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileType = string.Empty;
    [ObservableProperty] private long _fileSizeBytes;
    [ObservableProperty] private string _status = "pending";
    [ObservableProperty] private string _preview = string.Empty;
    [ObservableProperty] private string _suggestedCollectionName = string.Empty;
    [ObservableProperty] private string _suggestedTags = string.Empty;
    [ObservableProperty] private DateTime _addedAt;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _sourceType = string.Empty;
    [ObservableProperty] private string _sourceUrl = string.Empty;
    [ObservableProperty] private bool _isBrowserClip;
    [ObservableProperty] private bool _isFocused;
}
