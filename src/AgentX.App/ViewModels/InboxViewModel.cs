using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly IInboxService _inboxService;
    private readonly ICollectionService _collectionService;
    private readonly ILocalizationService _localization;

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
    [ObservableProperty] private string _focusedInboxSourceLabel = string.Empty;
    [ObservableProperty] private string _focusedInboxVisibilityHint = string.Empty;
    public bool HasFocusedInboxLanding => !string.IsNullOrWhiteSpace(FocusedInboxSourceLabel);

    // ── Collection Selection ─────────────────────────────────
    public ObservableCollection<CollectionEntity> Collections { get; } = new();
    [ObservableProperty] private CollectionEntity? _selectedCollection;

    // ── Filter Options ───────────────────────────────────────
    /// <summary>
    /// The STATUS list: each filter's value (what <see cref="StatusFilter"/> and the inbox
    /// service use) and the name shown for it in the user's language.
    /// </summary>
    public IReadOnlyList<InboxStatusFilterOption> StatusFilters { get; }

    private CancellationTokenSource? _previewCts;

    public InboxViewModel(
        IInboxService inboxService,
        ICollectionService collectionService,
        ILocalizationService localization,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _inboxService = inboxService;
        _collectionService = collectionService;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _operationsDrillInService = operationsDrillInService;

        StatusFilters = new[] { "pending", "accepted", "rejected", "deferred", "all" }
            .Select(value => new InboxStatusFilterOption(value, DescribeStatus(value)))
            .ToList();
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
            StatusMessage = _localization.GetString("Inbox_LoadFailed");
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
                    StatusLabel = DescribeStatus(item.Status),
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

            ReapplyFocusedInboxItem();
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

        var visibilityHint = string.Empty;
        var focusedItem = InboxItems.FirstOrDefault(item => item.Id == request.ItemId);
        if (focusedItem is null && StatusFilter != "all")
        {
            StatusFilter = "all";
            await LoadInboxItemsAsync();
            focusedItem = InboxItems.FirstOrDefault(item => item.Id == request.ItemId);
            if (focusedItem is not null)
            {
                visibilityHint = _localization.GetString("Inbox_FilterWidened");
            }
        }

        if (focusedItem is null)
        {
            StatusMessage = _localization.GetString("Inbox_RequestedItemGone");
            return;
        }

        FocusedInboxItemId = request.ItemId;
        FocusedInboxSourceLabel = request.SourceLabel;
        FocusedInboxVisibilityHint = visibilityHint;
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

    private void ReapplyFocusedInboxItem()
    {
        if (FocusedInboxItemId <= 0 || string.IsNullOrWhiteSpace(FocusedInboxSourceLabel))
        {
            ClearInboxItemFocus();
            return;
        }

        var focusedItem = InboxItems.FirstOrDefault(item => item.Id == FocusedInboxItemId);
        if (focusedItem is null)
        {
            ClearFocusedInboxLanding();
            return;
        }

        foreach (var item in InboxItems)
        {
            item.IsFocused = item.Id == FocusedInboxItemId;
        }

        var currentIndex = InboxItems.IndexOf(focusedItem);
        if (currentIndex > 0)
        {
            InboxItems.Move(currentIndex, 0);
        }
    }

    private void ClearInboxItemFocus()
    {
        foreach (var item in InboxItems)
        {
            item.IsFocused = false;
        }
    }

    private void ClearFocusedInboxLanding()
    {
        FocusedInboxItemId = 0;
        FocusedInboxSourceLabel = string.Empty;
        FocusedInboxVisibilityHint = string.Empty;
        ClearInboxItemFocus();
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
        var target = InboxItems.FirstOrDefault(item => item.Id == itemId);

        try
        {
            var result = await _inboxService.AcceptItemAsync(itemId, SelectedCollection?.Id);
            var resolvedFocusedItemMessage = BuildFocusedInboxResolutionMessage(
                itemId,
                target?.FileName,
                result.Outcome);

            await LoadInboxItemsAsync();
            if (resolvedFocusedItemMessage is not null)
            {
                ClearFocusedInboxLanding();
                StatusMessage = resolvedFocusedItemMessage;
            }
            else
            {
                StatusMessage = DescribeAcceptStatus(result.Outcome);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to accept inbox item {Id}", itemId);
            StatusMessage = _localization.GetString("Inbox_AcceptFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RejectItemAsync(long itemId)
    {
        try
        {
            await _inboxService.RejectItemAsync(itemId);
            await LoadInboxItemsAsync();
            StatusMessage = _localization.GetString("Inbox_ItemRejected");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reject inbox item {Id}", itemId);
            StatusMessage = _localization.GetString("Inbox_RejectFailed");
        }
    }

    [RelayCommand]
    private async Task DeferItemAsync(long itemId)
    {
        try
        {
            await _inboxService.DeferItemAsync(itemId);
            await LoadInboxItemsAsync();
            StatusMessage = _localization.GetString("Inbox_ItemDeferred");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to defer inbox item {Id}", itemId);
            StatusMessage = _localization.GetString("Inbox_DeferFailed");
        }
    }

    [RelayCommand]
    private async Task AcceptAllAsync()
    {
        IsProcessing = true;
        try
        {
            var result = await _inboxService.AcceptAllPendingAsync();
            await LoadInboxItemsAsync();
            StatusMessage = DescribeBatchAccept(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to accept all items");
            StatusMessage = _localization.GetString("Inbox_AcceptAllFailed");
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
            StatusMessage = _localization.GetString("Inbox_PreviewsGenerated");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.GetString("Inbox_PreviewsCancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate previews");
            StatusMessage = _localization.GetString("Inbox_PreviewsFailed");
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
            StatusMessage = _localization.GetString("Inbox_CleanedUp");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to cleanup processed items");
            StatusMessage = _localization.GetString("Inbox_CleanupFailed");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadInboxItemsAsync();
    }

    [RelayCommand]
    private void DismissFocusedInboxLanding()
    {
        var sourceLabel = FocusedInboxSourceLabel;
        ClearFocusedInboxLanding();

        if (string.Equals(StatusMessage, sourceLabel, StringComparison.Ordinal))
        {
            StatusMessage = string.Empty;
        }
    }

    /// <summary>
    /// The name shown for an inbox status, or for the "all" filter, in the user's language.
    /// A status this page does not know is shown as it is stored.
    /// </summary>
    internal string DescribeStatus(string status) => status switch
    {
        "pending" => _localization.GetString("Inbox_StatusPending"),
        "accepted" => _localization.GetString("Inbox_StatusAccepted"),
        "rejected" => _localization.GetString("Inbox_StatusRejected"),
        "deferred" => _localization.GetString("Inbox_StatusDeferred"),
        "all" => _localization.GetString("Inbox_StatusAll"),
        _ => status,
    };

    /// <summary>Status line for a single accept, worded after what actually happened.</summary>
    internal string DescribeAcceptStatus(InboxAcceptOutcome outcome) => outcome switch
    {
        InboxAcceptOutcome.Imported => _localization.GetString("Inbox_AcceptedQueued"),
        InboxAcceptOutcome.AlreadyInVault => _localization.GetString("Inbox_AcceptedLinked"),
        _ => _localization.GetString("Inbox_AlreadyAccepted"),
    };

    /// <summary>Status line for accept-all: counts what was imported, linked, and failed.</summary>
    internal string DescribeBatchAccept(InboxBatchAcceptResult result)
    {
        if (result.Accepted == 0 && result.Failed == 0)
        {
            return _localization.GetString("Inbox_NothingToAccept");
        }

        var message = result.Accepted == 1
            ? _localization.GetString("Inbox_AcceptedCountOne")
            : _localization.GetString("Inbox_AcceptedCountMany", result.Accepted);
        if (result.AlreadyInVault > 0)
        {
            message = _localization.GetString("Inbox_AcceptedWithVault", message, result.AlreadyInVault);
        }

        if (result.Failed > 0)
        {
            message = result.Failed == 1
                ? _localization.GetString("Inbox_AcceptedWithFailureOne", message)
                : _localization.GetString("Inbox_AcceptedWithFailuresMany", message, result.Failed);
            if (result.Errors.Count > 0)
            {
                message = _localization.GetString("Inbox_AcceptedFirstError", message, result.Errors[0]);
            }
        }

        return message;
    }

    /// <summary>
    /// The status line that replaces the Operations landing once its item is accepted: the item
    /// by name when it has one, and what accepting it did. Null when another item was accepted.
    /// </summary>
    private string? BuildFocusedInboxResolutionMessage(long itemId, string? fileName, InboxAcceptOutcome outcome)
    {
        if (FocusedInboxItemId != itemId || string.IsNullOrWhiteSpace(FocusedInboxSourceLabel))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return outcome switch
            {
                InboxAcceptOutcome.Imported => _localization.GetString("Inbox_ResolvedFocusedQueued"),
                InboxAcceptOutcome.AlreadyInVault => _localization.GetString("Inbox_ResolvedFocusedLinked"),
                _ => _localization.GetString("Inbox_ResolvedFocusedAlreadyAccepted"),
            };
        }

        return outcome switch
        {
            InboxAcceptOutcome.Imported => _localization.GetString("Inbox_ResolvedQueued", fileName),
            InboxAcceptOutcome.AlreadyInVault => _localization.GetString("Inbox_ResolvedLinked", fileName),
            _ => _localization.GetString("Inbox_ResolvedAlreadyAccepted", fileName),
        };
    }

    partial void OnFocusedInboxSourceLabelChanged(string value) =>
        OnPropertyChanged(nameof(HasFocusedInboxLanding));
}

/// <summary>A STATUS filter: the value the inbox is filtered by and the name shown for it.</summary>
public sealed record InboxStatusFilterOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public partial class InboxDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileType = string.Empty;
    [ObservableProperty] private long _fileSizeBytes;
    [ObservableProperty] private string _status = "pending";
    [ObservableProperty] private string _statusLabel = string.Empty;
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
