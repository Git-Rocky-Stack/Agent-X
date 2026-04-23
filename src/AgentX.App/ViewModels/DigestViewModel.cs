using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
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

    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private DigestReportDisplay? _currentReport;
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _statusMessage = "No digest reports yet";

    // ── Report History ────────────────────────────────────────
    public ObservableCollection<DigestReportDisplay> ReportHistory { get; } = new();

    public DigestViewModel(IDigestService digestService)
    {
        _digestService = digestService ?? throw new ArgumentNullException(nameof(digestService));
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
                ? $"Last generated {CurrentReport!.GeneratedAtFormatted}"
                : "No digest reports yet. Generate one to see your weekly summary.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load digest reports");
            StatusMessage = "Failed to load reports";
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
        StatusMessage = "Generating weekly digest...";

        try
        {
            var report = await _digestService.GenerateDigestAsync();
            var display = MapToDisplay(report);
            CurrentReport = display;
            HasReport = true;

            // Insert at the top of the history
            ReportHistory.Insert(0, display);

            StatusMessage = "Digest generated successfully";
            Log.Information("Digest report generated via UI");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate digest");
            StatusMessage = "Failed to generate digest";
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

    private static DigestReportDisplay MapToDisplay(DigestReportEntity entity)
    {
        var display = new DigestReportDisplay
        {
            Id = entity.Id,
            GeneratedAt = entity.GeneratedAt,
            PeriodStart = entity.PeriodStart,
            PeriodEnd = entity.PeriodEnd,
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
    public string GeneratedAtFormatted =>
        GeneratedAt.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt");

    public string PeriodFormatted =>
        $"{PeriodStart.ToLocalTime():MMM d} - {PeriodEnd.ToLocalTime():MMM d, yyyy}";

    public string TokensFormatted =>
        TotalTokensUsed > 1000 ? $"{TotalTokensUsed / 1000.0:F1}K" : TotalTokensUsed.ToString();

    public string ShortPeriodFormatted =>
        $"{PeriodStart.ToLocalTime():MMM d} - {PeriodEnd.ToLocalTime():MMM d}";
}

// ── JSON Deserialization Models ──────────────────────────────────

public class TopSearchItem
{
    public string Query { get; set; } = string.Empty;
    public int Count { get; set; }
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;
    public string TrendLabel => DigestTrendFormatter.FormatTrendLabel(Trend, DeltaCount, PreviousCount);
}

public class TopCollectionItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public int DocumentCount => Count;
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;
    public string TrendLabel => DigestTrendFormatter.FormatTrendLabel(Trend, DeltaCount, PreviousCount);
}

public class FileTypeItem
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public int PreviousCount { get; set; }
    public int DeltaCount { get; set; }
    public string Trend { get; set; } = string.Empty;
    public string TrendLabel => DigestTrendFormatter.FormatTrendLabel(Trend, DeltaCount, PreviousCount);
}

internal static class DigestTrendFormatter
{
    public static string FormatTrendLabel(string trend, int deltaCount, int previousCount)
    {
        return trend switch
        {
            "new" => "new this period",
            "up" => $"+{deltaCount} vs prior period",
            "down" => $"{deltaCount} vs prior period",
            _ when previousCount > 0 => "flat vs prior period",
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
