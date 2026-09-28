using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// Shared ViewModel for export operations. Can be used from any page
/// that needs to export conversations, search results, or collections.
/// Not a standalone page - used as a helper in ChatViewModel, SearchViewModel, etc.
/// </summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly IExportService _exportService;
    private readonly ILocalizationService _localization;

    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private ExportFormat _selectedFormat = ExportFormat.Markdown;
    [ObservableProperty] private bool _includeCitations = true;
    [ObservableProperty] private bool _includeMetadata = true;
    [ObservableProperty] private bool _includeTimestamps = true;
    [ObservableProperty] private bool _includeModelInfo;
    [ObservableProperty] private string? _lastExportPath;

    /// <summary>
    /// Whether the most recent export or copy succeeded, so a page can show the outcome
    /// (and <see cref="LastExportPath"/>) without parsing <see cref="StatusMessage"/>.
    /// </summary>
    [ObservableProperty] private bool _lastExportSucceeded;

    public List<ExportFormat> AvailableFormats { get; } = new()
    {
        ExportFormat.Markdown,
        ExportFormat.Html,
        ExportFormat.Pdf,
        ExportFormat.Json,
        ExportFormat.PlainText,
        ExportFormat.Csv
    };

    public ExportViewModel(IExportService exportService, ILocalizationService localization)
    {
        _exportService = exportService;
        _localization = localization;
    }

    [RelayCommand]
    private async Task ExportConversationAsync(ExportConversationRequest request)
    {
        if (request is null) return;

        IsExporting = true;
        LastExportSucceeded = false;
        StatusMessage = _localization.GetString("Export_ExportingConversation");

        try
        {
            var options = request.Options ?? BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportConversationAsync(
                request.ConversationId, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                LastExportSucceeded = true;
                StatusMessage = _localization.GetString("Export_ExportedTo", Path.GetFileName(result.FilePath) ?? string.Empty);
            }
            else
            {
                StatusMessage = _localization.GetString("Export_Failed", result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export conversation {Id}", request.ConversationId);
            StatusMessage = _localization.GetString("Export_Failed", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportConversationsAsync(ExportBatchRequest request)
    {
        if (request is null || request.ConversationIds.Count == 0) return;

        var count = request.ConversationIds.Count;
        IsExporting = true;
        LastExportSucceeded = false;
        StatusMessage = count == 1
            ? _localization.GetString("Export_ExportingConversationsOne", count)
            : _localization.GetString("Export_ExportingConversationsMany", count);

        try
        {
            var options = BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportConversationsAsync(
                request.ConversationIds, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                LastExportSucceeded = true;
                StatusMessage = count == 1
                    ? _localization.GetString("Export_ExportedConversationsOne", count)
                    : _localization.GetString("Export_ExportedConversationsMany", count);
            }
            else
            {
                StatusMessage = _localization.GetString("Export_Failed", result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Batch conversation export failed");
            StatusMessage = _localization.GetString("Export_Failed", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportCollectionAsync(ExportCollectionRequest request)
    {
        if (request is null) return;

        IsExporting = true;
        LastExportSucceeded = false;
        StatusMessage = _localization.GetString("Export_ExportingCollection");

        try
        {
            var options = BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportCollectionAsync(
                request.CollectionId, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                LastExportSucceeded = true;
                StatusMessage = _localization.GetString("Export_CollectionExportedTo", Path.GetFileName(result.FilePath) ?? string.Empty);
            }
            else
            {
                StatusMessage = _localization.GetString("Export_Failed", result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export collection {Id}", request.CollectionId);
            StatusMessage = _localization.GetString("Export_Failed", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>
    /// Copies a conversation as formatted Markdown to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyConversationAsMarkdownAsync(long conversationId)
    {
        LastExportSucceeded = false;

        try
        {
            var markdown = await _exportService.FormatConversationAsMarkdownAsync(conversationId, IncludeMetadata);
            if (string.IsNullOrEmpty(markdown))
            {
                // Nothing was formatted (the conversation no longer exists): say so rather
                // than report a copy and leave the clipboard as it was.
                StatusMessage = _localization.GetString("Export_CopyFailedNotFound");
                return;
            }

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(markdown);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            LastExportSucceeded = true;
            StatusMessage = _localization.GetString("Export_CopiedAsMarkdown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy conversation as Markdown");
            StatusMessage = _localization.GetString("Export_CopyFailed", ex.Message);
        }
    }


    private ExportOptions BuildOptions(string? outputPath, string? title) => new()
    {
        Format = SelectedFormat,
        IncludeCitations = IncludeCitations,
        IncludeMetadata = IncludeMetadata,
        IncludeTimestamps = IncludeTimestamps,
        IncludeModelInfo = IncludeModelInfo,
        OutputPath = outputPath,
        Title = title
    };
}

// -- Request Models ----------------------------------------------

public record ExportConversationRequest(long ConversationId, ExportOptions? Options = null, string? OutputPath = null, string? Title = null);
public record ExportBatchRequest(IReadOnlyList<long> ConversationIds, string? OutputPath = null, string? Title = null);
public record ExportCollectionRequest(long CollectionId, string? OutputPath = null, string? Title = null);
