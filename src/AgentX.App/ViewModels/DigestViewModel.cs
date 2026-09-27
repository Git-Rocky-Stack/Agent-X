using System.Collections.ObjectModel;
using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// DIGEST VIEW MODEL
//
// Manages the weekly digest report page. Loads existing reports, generates
// new ones on demand, and presents parsed report data for display.
// ═══════════════════════════════════════════════════════════════════════════

public partial class DigestViewModel : ObservableObject
{
    private readonly IDigestService _digestService;
    private readonly ILocalizationService _localization;

    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private DigestReportDisplay? _currentReport;
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Report History ────────────────────────────────────────
    public ObservableCollection<DigestReportDisplay> ReportHistory { get; } = new();

    public DigestViewModel(IDigestService digestService, ILocalizationService localization)
    {
        _digestService = digestService ?? throw new ArgumentNullException(nameof(digestService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        StatusMessage = _localization.GetString("Digest_NoReportsYet");
    }

    // ═══════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Load the latest report
            var latest = await _digestService.GetLatestReportAsync();
            if (latest is not null)
            {
                CurrentReport = MapToDisplay(latest);
                HasReport = true;

                // Mark as read when viewed
                if (!latest.IsRead)
                {
                    await _digestService.MarkAsReadAsync(latest.Id);
                }
            }

            // Load report history
            var history = await _digestService.GetReportHistoryAsync(10);
            ReportHistory.Clear();
            foreach (var report in history)
            {
                ReportHistory.Add(MapToDisplay(report));
            }

            StatusMessage = HasReport
                ? _localization.GetString("Digest_LastGenerated", CurrentReport!.GeneratedAtFormatted)
                : _localization.GetString("Digest_NoReportsYetGenerateOne");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load digest reports");
            StatusMessage = _localization.GetString("Digest_LoadReportsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a new weekly digest report covering the past 7 days.
    /// </summary>
    [RelayCommand]
    private async Task GenerateDigestAsync()
    {
        IsGenerating = true;
        StatusMessage = _localization.GetString("Digest_GeneratingDigest");

        try
        {
            var report = await _digestService.GenerateDigestAsync();
            var display = MapToDisplay(report);
            CurrentReport = display;
            HasReport = true;

            // Insert at the top of the history
            ReportHistory.Insert(0, display);

            StatusMessage = _localization.GetString("Digest_DigestGenerated");
            Log.Information("Digest report generated via UI");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate digest");
            StatusMessage = _localization.GetString("Digest_GenerateDigestFailed");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// Selects a report from the history list for viewing.
    /// </summary>
    [RelayCommand]
    private void SelectReport(DigestReportDisplay? report)
    {
        if (report is not null)
        {
            CurrentReport = report;
            HasReport = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // MAPPING
    // ═══════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private DigestReportDisplay MapToDisplay(DigestReportEntity entity)
    {
        var display = new DigestReportDisplay
        {
            Id = entity.Id,
            GeneratedAt = entity.GeneratedAt,
            // The date patterns are in the resources, so each language orders the date and
            // time its own way (en-US: "MMM d, yyyy 'at' h:mm tt").
            GeneratedAtFormatted = _localization.GetString("Digest_GeneratedAtFormat", entity.GeneratedAt.ToLocalTime()),
            PeriodStart = entity.PeriodStart,
            PeriodEnd = entity.PeriodEnd,
            PeriodFormatted = _localization.GetString(
                "Digest_PeriodFormat", entity.PeriodStart.ToLocalTime(), entity.PeriodEnd.ToLocalTime()),
            ShortPeriodFormatted = _localization.GetString(
                "Digest_ShortPeriodFormat", entity.PeriodStart.ToLocalTime(), entity.PeriodEnd.ToLocalTime()),
            NewDocumentsCount = entity.NewDocumentsCount,
            NewConversationsCount = entity.NewConversationsCount,
            TotalSearches = entity.TotalSearches,
            TotalTokensUsed = entity.TotalTokensUsed,
            StorageDelta = FormatStorageDelta(entity.StorageDeltaBytes),
            IsRead = entity.IsRead
        };

        // Parse JSON detail fields with graceful degradation
        try
        {
            if (!string.IsNullOrEmpty(entity.TopSearchesJson))
            {
                display.TopSearches = JsonSerializer.Deserialize<List<TopSearchItem>>(
                    entity.TopSearchesJson, _jsonOptions) ?? new();
            }

            if (!string.IsNullOrEmpty(entity.TopCollectionsJson))
            {
                display.TopCollections = JsonSerializer.Deserialize<List<TopCollectionItem>>(
                    entity.TopCollectionsJson, _jsonOptions) ?? new();
            }

            if (!string.IsNullOrEmpty(entity.FileTypeBreakdownJson))
            {
                display.FileTypeBreakdown = JsonSerializer.Deserialize<List<FileTypeItem>>(
                    entity.FileTypeBreakdownJson, _jsonOptions) ?? new();
            }

            if (!string.IsNullOrEmpty(entity.HighlightsJson))
            {
                display.Highlights = JsonSerializer.Deserialize<List<HighlightItem>>(
                    entity.HighlightsJson, _jsonOptions) ?? new();
            }
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to parse JSON detail fields for digest report {ReportId}", entity.Id);
        }

        foreach (var item in display.TopSearches)
        {
            item.TrendLabel = DigestTrendFormatter.FormatTrendLabel(_localization, item.Trend, item.DeltaCount, item.PreviousCount);
        }

        foreach (var item in display.TopCollections)
        {
            item.TrendLabel = DigestTrendFormatter.FormatTrendLabel(_localization, item.Trend, item.DeltaCount, item.PreviousCount);
        }

        foreach (var item in display.FileTypeBreakdown)
        {
            item.TrendLabel = DigestTrendFormatter.FormatTrendLabel(_localization, item.Trend, item.DeltaCount, item.PreviousCount);
        }

        return display;
    }

    private static string FormatStorageDelta(long bytes)
    {
        var prefix = bytes >= 0 ? "+" : "";
        var abs = Math.Abs(bytes);

        return abs switch
        {
            0 => "0 B",
            < 1024 => $"{prefix}{bytes} B",
            < 1_048_576 => $"{prefix}{bytes / 1024.0:F1} KB",
            _ => $"{prefix}{bytes / 1_048_576.0:F1} MB"
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DISPLAY MODELS
//
// Presentation-layer models for binding digest report data to the UI.
// Separate from the entity to provide formatted strings and parsed JSON data.
// ═══════════════════════════════════════════════════════════════════════════

public class DigestReportDisplay
{
    public long Id { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int NewDocumentsCount { get; set; }
    public int NewConversationsCount { get; set; }
    public int TotalSearches { get; set; }
    public int TotalTokensUsed { get; set; }
    public string StorageDelta { get; set; } = string.Empty;
    public bool IsRead { get; set; }

    // Parsed JSON detail data
    public List<TopSearchItem> TopSearches { get; set; } = new();
    public List<TopCollectionItem> TopCollections { get; set; } = new();
    public List<FileTypeItem> FileTypeBreakdown { get; set; } = new();
    public List<HighlightItem> Highlights { get; set; } = new();

    // ── Formatted Properties for Display ────────────────────────

    // The date texts are set by the view model, in local time and the UI language.

    /// <summary>When the report was generated.</summary>
    public string GeneratedAtFormatted { get; set; } = string.Empty;

    /// <summary>The period the report covers, with the year.</summary>
    public string PeriodFormatted { get; set; } = string.Empty;

    public string TokensFormatted =>
        TotalTokensUsed > 1000 ? $"{TotalTokensUsed / 1000.0:F1}K" : TotalTokensUsed.ToString();

    /// <summary>The period the report covers, without the year.</summary>
    public string ShortPeriodFormatted { get; set; } = string.Empty;
}

// ── JSON Deserialization Models ──────────────────────────────────

public class TopSearchItem
{
    public string Query { get; set; } = string.Empty;
    public int Count { get; set; }
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;

    /// <summary>The trend in words, set by the view model in the UI language.</summary>
    public string TrendLabel { get; set; } = string.Empty;
}

public class TopCollectionItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public int DocumentCount => Count;
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;

    /// <summary>The trend in words, set by the view model in the UI language.</summary>
    public string TrendLabel { get; set; } = string.Empty;
}

public class FileTypeItem
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;

    /// <summary>The trend in words, set by the view model in the UI language.</summary>
    public string TrendLabel { get; set; } = string.Empty;
}

internal static class DigestTrendFormatter
{
    public static string FormatTrendLabel(ILocalizationService localization, string trend, int deltaCount, int previousCount)
    {
        return trend switch
        {
            "new" => localization.GetString("Digest_TrendNew"),
            "up" => localization.GetString("Digest_TrendChange", $"+{deltaCount}"),
            "down" => localization.GetString("Digest_TrendChange", deltaCount),
            _ when previousCount > 0 => localization.GetString("Digest_TrendFlat"),
            _ => string.Empty
        };
    }
}

public class HighlightItem
{
    public string Title { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int TokensUsed { get; set; }
}
