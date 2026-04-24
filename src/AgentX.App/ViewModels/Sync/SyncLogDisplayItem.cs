using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels.Sync;

// =============================================================================
// SYNC LOG DISPLAY ITEM
//
// Observable presentation wrapper around SyncLogEntity for the sync history
// list. DirectionGlyph and StatusLabel are computed from the observable fields
// so they update automatically when Direction or IsSuccess change.
// =============================================================================

/// <summary>
/// Presentation model for a single entry in the sync history list.
/// Maps raw <see cref="AgentX.Core.Data.Entities.SyncLogEntity"/> fields to
/// display-ready strings and Segoe Fluent Icons glyphs.
/// </summary>
public partial class SyncLogDisplayItem : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _direction = string.Empty;
    [ObservableProperty] private int _changesApplied;
    [ObservableProperty] private int _conflictsDetected;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private string _syncedAtFormatted = string.Empty;
    [ObservableProperty] private string _durationFormatted = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isFocused;

    /// <summary>
    /// Full formatted timestamp for display, e.g. "Mar 7, 2026 3:45 PM".
    /// Populated by MapToDisplayItem from the raw SyncLogEntity.SyncedAt value.
    /// </summary>
    [ObservableProperty] private string _syncedAtFull = string.Empty;

    // ── Existing Computed Properties ──────────────────────────────────────────

    /// <summary>
    /// Segoe Fluent Icons glyph representing the sync direction.
    /// Export (outbound, local->folder) uses the Upload glyph U+E898.
    /// Import (inbound, folder->local) uses the Download glyph U+E896.
    /// </summary>
    public string DirectionGlyph => Direction.Equals("export", StringComparison.OrdinalIgnoreCase)
        ? "\uE898"   // Upload / Send
        : "\uE896";  // Download / Receive

    /// <summary>
    /// Short uppercase label for status badge display.
    /// </summary>
    public string StatusLabel => IsSuccess ? "SUCCESS" : "FAILED";

    // ── Properties Required by SyncSettingsPage.xaml DataTemplate ─────────────

    /// <summary>
    /// Status color: green (#22C55E) for success, red (#EF4444) for failure.
    /// Bound by the XAML DataTemplate via SolidColorBrush Color="{x:Bind StatusColor}".
    /// </summary>
    public Windows.UI.Color StatusColor => IsSuccess
        ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)   // Green — success
        : Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44);  // Red   — failure

    /// <summary>
    /// Segoe Fluent Icons glyph for the status indicator.
    /// Checkmark (U+E73E) for success, X mark (U+E711) for failure.
    /// </summary>
    public string StatusIcon => IsSuccess
        ? "\uE73E"   // Checkmark
        : "\uE711";  // X mark

    /// <summary>
    /// Segoe Fluent Icons glyph for sync direction — alias for DirectionGlyph.
    /// Upload (U+E898) for export, Download (U+E896) for import.
    /// </summary>
    public string DirectionIcon => DirectionGlyph;

    /// <summary>
    /// Human-readable direction label: "Export" or "Import".
    /// </summary>
    public string DirectionDisplay => Direction.Equals("export", StringComparison.OrdinalIgnoreCase)
        ? "Export"
        : "Import";

    /// <summary>
    /// Mixed-case status text: "Success" or "Failed".
    /// </summary>
    public string StatusText => IsSuccess ? "Success" : "Failed";

    /// <summary>
    /// True when one or more conflicts were detected during this sync pass.
    /// Controls visibility of the conflicts indicator in the DataTemplate.
    /// </summary>
    public bool HasConflicts => ConflictsDetected > 0;

    /// <summary>
    /// True when an error message is present.
    /// Controls visibility of the error text block in the DataTemplate.
    /// </summary>
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
}
