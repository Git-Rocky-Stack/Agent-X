using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels.Sync;

// =============================================================================
// SYNC LOG DISPLAY ITEM
//
// Observable presentation wrapper around SyncLogEntity for the sync history
// list. DirectionGlyph and StatusLabel are computed from the observable fields
// so they update automatically when Direction or IsSuccess change. The texts
// shown for the direction and status are the labels the view model supplies
// in the user's language; Direction keeps the stored token.
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

    /// <summary>Direction badge text for an export pass, in the user's language.</summary>
    public string ExportLabel { get; init; } = string.Empty;

    /// <summary>Direction badge text for an import pass, in the user's language.</summary>
    public string ImportLabel { get; init; } = string.Empty;

    /// <summary>Status text for a pass that succeeded, in the user's language.</summary>
    public string SuccessLabel { get; init; } = string.Empty;

    /// <summary>Status text for a pass that failed, in the user's language.</summary>
    public string FailedLabel { get; init; } = string.Empty;

    // -- Existing Computed Properties ------------------------------------------

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
    public string StatusLabel => StatusText.ToUpperInvariant();

    // -- Properties Required by SyncSettingsPage.xaml DataTemplate -------------

    /// <summary>
    /// Status color: green (#41E25E) for success, red (#C8453E) for failure.
    /// Bound by the XAML DataTemplate via SolidColorBrush Color="{x:Bind StatusColor}".
    /// </summary>
    public Windows.UI.Color StatusColor => IsSuccess
        ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)   // Green - success
        : Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44);  // Red - failure

    /// <summary>
    /// Segoe Fluent Icons glyph for the status indicator.
    /// Checkmark (U+E73E) for success, X mark (U+E711) for failure.
    /// </summary>
    public string StatusIcon => IsSuccess
        ? "\uE73E"   // Checkmark
        : "\uE711";  // X mark

    /// <summary>
    /// Segoe Fluent Icons glyph for sync direction - alias for DirectionGlyph.
    /// Upload (U+E898) for export, Download (U+E896) for import.
    /// </summary>
    public string DirectionIcon => DirectionGlyph;

    /// <summary>
    /// Human-readable direction label: <see cref="ExportLabel"/> or <see cref="ImportLabel"/>.
    /// </summary>
    public string DirectionDisplay => Direction.Equals("export", StringComparison.OrdinalIgnoreCase)
        ? ExportLabel
        : ImportLabel;

    /// <summary>
    /// Mixed-case status text: <see cref="SuccessLabel"/> or <see cref="FailedLabel"/>.
    /// </summary>
    public string StatusText => IsSuccess ? SuccessLabel : FailedLabel;

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
