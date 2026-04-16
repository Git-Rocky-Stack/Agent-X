using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using AgentX.App.ViewModels;
using AgentX.Core.Search.Models;
using Serilog;
using Windows.System;
using Windows.UI;

namespace AgentX.App.Views;

/// <summary>
/// Premium Semantic Search page with search bar, filter chips, results list,
/// and search history sidebar. Integrates with SearchViewModel for all
/// data binding and command execution.
/// </summary>
public sealed partial class SearchPage : Page
{
    /// <summary>
    /// All filter chip buttons, keyed by their Tag value.
    /// Used to swap active/inactive styles when a chip is clicked.
    /// </summary>
    private readonly Dictionary<string, Button> _filterChips = new();

    /// <summary>
    /// The currently active filter tag. Empty string means "All Types".
    /// </summary>
    private string _activeFilterTag = "";

    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        ViewModel = App.GetService<SearchViewModel>();
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    // =================================================================
    // LIFECYCLE
    // =================================================================

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("SearchPage loaded");

        // Register filter chip buttons for style toggling
        _filterChips[""] = FilterAll;
        _filterChips["pdf"] = FilterPdf;
        _filterChips["docx"] = FilterDocx;
        _filterChips["txt"] = FilterTxt;
        _filterChips["code"] = FilterCode;
        _filterChips["md"] = FilterMd;
        _filterChips["CalendarEvent"] = FilterCalendar;
        _filterChips["EmailMessage"] = FilterEmail;

        await ViewModel.InitializeAsync();

        // Focus the search input for immediate typing
        SearchInputBox.Focus(FocusState.Programmatic);
    }

    // =================================================================
    // KEYBOARD INPUT HANDLING
    // =================================================================

    /// <summary>
    /// Handles Enter key in the search TextBox to trigger search.
    /// </summary>
    private void SearchInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;

            if (ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
        }
    }

    // =================================================================
    // FILTER CHIP HANDLING
    // =================================================================

    /// <summary>
    /// Handles filter chip clicks. Swaps the active style between chips
    /// and triggers the FilterByFileType command.
    /// </summary>
    private void OnFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var tag = button.Tag?.ToString() ?? "";

        // Skip if already active
        if (tag == _activeFilterTag)
            return;

        // Deactivate the previously active chip
        if (_filterChips.TryGetValue(_activeFilterTag, out var previousChip))
        {
            previousChip.Style = (Style)Resources["FilterChipStyle"]
                ?? (Style)Application.Current.Resources["FilterChipStyle"];
        }

        // Activate the new chip
        button.Style = (Style)Resources["FilterChipActiveStyle"]
            ?? (Style)Application.Current.Resources["FilterChipActiveStyle"];

        _activeFilterTag = tag;

        // Execute filter command
        var filterValue = string.IsNullOrEmpty(tag) ? null : tag;
        if (ViewModel.FilterByFileTypeCommand.CanExecute(filterValue))
        {
            ViewModel.FilterByFileTypeCommand.Execute(filterValue);
        }
    }

    // =================================================================
    // SEARCH MODE TOGGLE
    // =================================================================

    /// <summary>
    /// Handles the search mode RadioButton toggle. Parses the Tag string
    /// to a <see cref="SearchMode"/> enum value and updates the ViewModel.
    /// </summary>
    private void OnSearchModeChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string modeStr)
        {
            ViewModel.SearchMode = modeStr switch
            {
                "Keyword" => SearchMode.Keyword,
                "Hybrid" => SearchMode.Hybrid,
                _ => SearchMode.Semantic
            };

            Log.Debug("Search mode changed to {Mode}", ViewModel.SearchMode);
        }
    }

    // =================================================================
    // HISTORY ITEM CLICK
    // =================================================================

    /// <summary>
    /// Handles clicks on search history items to re-execute that query.
    /// </summary>
    private void OnHistoryItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchHistoryItem historyItem)
        {
            if (ViewModel.SelectHistoryItemCommand.CanExecute(historyItem.QueryText))
            {
                ViewModel.SelectHistoryItemCommand.Execute(historyItem.QueryText);
            }
        }
    }

    // =================================================================
    // RESULT ACTIONS
    // =================================================================

    /// <summary>
    /// Opens the document in Windows Explorer.
    /// </summary>
    private void OnOpenDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long documentId })
        {
            if (ViewModel.OpenDocumentCommand.CanExecute(documentId))
            {
                ViewModel.OpenDocumentCommand.Execute(documentId);
            }
        }
    }

    /// <summary>
    /// Clears filters and resets to "All Types".
    /// </summary>
    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        // Reset active filter to "All"
        if (_filterChips.TryGetValue(_activeFilterTag, out var previousChip))
        {
            previousChip.Style = (Style)Resources["FilterChipStyle"]
                ?? (Style)Application.Current.Resources["FilterChipStyle"];
        }

        _activeFilterTag = "";
        FilterAll.Style = (Style)Resources["FilterChipActiveStyle"]
            ?? (Style)Application.Current.Resources["FilterChipActiveStyle"];

        if (ViewModel.FilterByFileTypeCommand.CanExecute(null))
        {
            ViewModel.FilterByFileTypeCommand.Execute(null);
        }
    }

    // =================================================================
    // COLLECTION FILTER HANDLING
    // =================================================================

    private void OnCollectionFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is CollectionFilterItem item)
        {
            ViewModel.SelectedCollectionFilterId = item.Id;

            if (!string.IsNullOrWhiteSpace(ViewModel.QueryText))
            {
                if (ViewModel.SearchCommand.CanExecute(null))
                    ViewModel.SearchCommand.Execute(null);
            }
        }
    }

    // =================================================================
    // SAVED FILTER ACTIONS
    // =================================================================

    private void OnApplySavedFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SavedFilterItem filter })
        {
            if (ViewModel.ApplySavedFilterCommand.CanExecute(filter))
                ViewModel.ApplySavedFilterCommand.Execute(filter);
        }
    }

    private void OnRemoveSavedFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long filterId })
        {
            if (ViewModel.RemoveSavedFilterCommand.CanExecute(filterId))
                ViewModel.RemoveSavedFilterCommand.Execute(filterId);
        }
    }

    // =================================================================
    // STATIC HELPERS for x:Bind in DataTemplate
    // =================================================================

    /// <summary>
    /// Formats a percentage value for display in the advanced filters panel.
    /// </summary>
    public static string FormatPercent(double value) => $"{value:F0}%";

    /// <summary>
    /// Calculates the width of the relevance score bar based on percentage.
    /// Max bar width is 150px.
    /// </summary>
    public static double GetScoreWidth(int relevancePercent)
    {
        return Math.Clamp(relevancePercent, 0, 100) / 100.0 * 150.0;
    }

    /// <summary>
    /// Returns a Color for the relevance score bar based on score tier.
    /// </summary>
    public static Color GetScoreColorValue(int relevancePercent)
    {
        return relevancePercent switch
        {
            >= 80 => ColorHelper.FromArgb(255, 76, 175, 80),   // #4CAF50 Green
            >= 60 => ColorHelper.FromArgb(255, 255, 193, 7),   // #FFC107 Yellow
            >= 40 => ColorHelper.FromArgb(255, 255, 152, 0),   // #FF9800 Orange
            _ => ColorHelper.FromArgb(255, 244, 67, 54)        // #F44336 Red
        };
    }

    /// <summary>
    /// Returns Visibility.Visible if pageNumber is not null, else Collapsed.
    /// </summary>
    public static Visibility PageNumberToVisibility(int? pageNumber)
    {
        return pageNumber.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Determines whether the empty state should be shown.
    /// Visible only when: not searching, no results, and no "no results" state.
    /// </summary>
    public Visibility ShowEmptyState(bool hasResults, bool showNoResults, bool isSearching)
    {
        if (hasResults || showNoResults || isSearching)
            return Visibility.Collapsed;

        return Visibility.Visible;
    }
}
