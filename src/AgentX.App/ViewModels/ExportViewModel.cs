using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// Shared ViewModel for export operations. Can be used from any page
/// that needs to export conversations, search results, or collections.
/// Not a standalone page — used as a helper in ChatViewModel, SearchViewModel, etc.
/// </summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly IExportService _exportService;

    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private ExportFormat _selectedFormat = ExportFormat.Markdown;
    [ObservableProperty] private bool _includeCitations = true;
    [ObservableProperty] private bool _includeMetadata = true;
    [ObservableProperty] private bool _includeTimestamps = true;
    [ObservableProperty] private bool _includeModelInfo;
    [ObservableProperty] private string? _lastExportPath;

    public List<ExportFormat> AvailableFormats { get; } = new()
    {
        ExportFormat.Markdown,
        ExportFormat.Html,
        ExportFormat.Pdf,
        ExportFormat.Json,
        ExportFormat.PlainText,
        ExportFormat.Csv
    };

    public ExportViewModel(IExportService exportService)
    {
        _exportService = exportService;
    }

    [RelayCommand]
    private async Task ExportConversationAsync(ExportConversationRequest request)
    {
        if (request is null) return;

        IsExporting = true;
        StatusMessage = "Exporting conversation...";

        try
        {
            var options = BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportConversationAsync(
                request.ConversationId, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                StatusMessage = $"Exported to {Path.GetFileName(result.FilePath)}";
            }
            else
            {
                StatusMessage = $"Export failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export conversation {Id}", request.ConversationId);
            StatusMessage = $"Export failed: {ex.Message}";
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

        IsExporting = true;
        StatusMessage = $"Exporting {request.ConversationIds.Count} conversations...";

        try
        {
            var options = BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportConversationsAsync(
                request.ConversationIds, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                StatusMessage = $"Exported {request.ConversationIds.Count} conversations";
            }
            else
            {
                StatusMessage = $"Export failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Batch conversation export failed");
            StatusMessage = $"Export failed: {ex.Message}";
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
        StatusMessage = "Exporting collection...";

        try
        {
            var options = BuildOptions(request.OutputPath, request.Title);
            var result = await _exportService.ExportCollectionAsync(
                request.CollectionId, options, CancellationToken.None);

            if (result.Success)
            {
                LastExportPath = result.FilePath;
                StatusMessage = $"Collection exported to {Path.GetFileName(result.FilePath)}";
            }
            else
            {
                StatusMessage = $"Export failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export collection {Id}", request.CollectionId);
            StatusMessage = $"Export failed: {ex.Message}";
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
        try
        {
            var markdown = await _exportService.FormatConversationAsMarkdown(conversationId, IncludeMetadata);
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(markdown);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            StatusMessage = "Conversation copied to clipboard as Markdown";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy conversation as Markdown");
            StatusMessage = "Copy failed";
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

// ── Request Models ──────────────────────────────────────────────

public record ExportConversationRequest(long ConversationId, string? OutputPath = null, string? Title = null);
public record ExportBatchRequest(IReadOnlyList<long> ConversationIds, string? OutputPath = null, string? Title = null);
public record ExportCollectionRequest(long CollectionId, string? OutputPath = null, string? Title = null);
