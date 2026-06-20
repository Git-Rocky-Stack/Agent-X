using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace AgentX.App.Controls;

/// <summary>
/// A VS Code-style command palette overlay that provides quick access to pages and actions.
/// Activated via Ctrl+K, supports fuzzy filtering, keyboard navigation, and animated transitions.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────
    private const double AnimationDurationMs = 200;

    // ── State ─────────────────────────────────────────────────────────
    private bool _isOpen;
    private int _selectedIndex = -1;
    private List<CommandItem> _allItems = new();
    private List<CommandItem> _filteredItems = new();
    private readonly List<Border> _renderedItemBorders = new();

    // ── Callbacks ─────────────────────────────────────────────────────
    /// <summary>
    /// Delegate invoked when the user selects a page navigation command.
    /// The string parameter is the page tag (e.g., "Dashboard", "Chat").
    /// </summary>
    public Action<string>? NavigateToPageRequested { get; set; }

    /// <summary>
    /// Delegate invoked when the user selects a general action command.
    /// The string parameter is the action identifier (e.g., "NewConversation").
    /// </summary>
    public Action<string>? ExecuteActionRequested { get; set; }

    /// <summary>
    /// Gets whether the command palette is currently visible.
    /// </summary>
    public bool IsOpen => _isOpen;

    public CommandPalette()
    {
        InitializeComponent();
        BuildCommandItems();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMMAND ITEM REGISTRY
    // ═══════════════════════════════════════════════════════════════════

    private void BuildCommandItems()
    {
        _allItems = new List<CommandItem>
        {
            // ── Pages ─────────────────────────────────────────────
            new("Dashboard", "View system overview and analytics", "Pages", "\uE80F", "Dashboard", CommandItemKind.Page, ""),
            new("AI Chat", "Start a conversation with the AI assistant", "Pages", "\uE8BD", "Chat", CommandItemKind.Page, "Ctrl+N"),
            new("Knowledge Vault", "Browse and manage your documents", "Pages", "\uE8F1", "KnowledgeVault", CommandItemKind.Page, "Ctrl+I"),
            new("Collections", "Organize documents into collections", "Pages", "\uF168", "Collections", CommandItemKind.Page, ""),
            new("Semantic Search", "Search across all your knowledge", "Pages", "\uE773", "Search", CommandItemKind.Page, "Ctrl+F"),
            new("Ask Your Files", "Query your documents with AI", "Pages", "\uE721", "AskFiles", CommandItemKind.Page, ""),
            new("Model Manager", "Download and configure AI models", "Pages", "\uE964", "ModelManager", CommandItemKind.Page, ""),
            new("Hardware Advisor", "Check hardware compatibility", "Pages", "\uE950", "HardwareAdvisor", CommandItemKind.Page, ""),
            new("Settings", "Configure application preferences", "Pages", "\uE713", "Settings", CommandItemKind.Page, "Ctrl+,"),

            // ── Actions ───────────────────────────────────────────
            new("New Conversation", "Start a fresh AI chat session", "Actions", "\uE8E5", "NewConversation", CommandItemKind.Action, "Ctrl+N"),
            new("Import Files", "Add documents to the Knowledge Vault", "Actions", "\uE8B5", "ImportFiles", CommandItemKind.Action, "Ctrl+I"),
            new("Refresh Dashboard", "Reload dashboard statistics", "Actions", "\uE72C", "RefreshDashboard", CommandItemKind.Action, ""),
            new("Toggle Theme", "Switch between dark and light modes", "Actions", "\uE793", "ToggleTheme", CommandItemKind.Action, ""),
        };

        _filteredItems = new List<CommandItem>(_allItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHOW / HIDE WITH ANIMATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the command palette with a fade-in and slide-down animation.
    /// Resets the search text and selection state.
    /// </summary>
    public void Show()
    {
        if (_isOpen) return;
        _isOpen = true;

        // Reset state
        SearchInput.Text = string.Empty;
        _filteredItems = new List<CommandItem>(_allItems);
        _selectedIndex = 0;
        RenderResults();

        // Make visible before animating
        Visibility = Visibility.Visible;

        // Animate backdrop fade in
        var backdropFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(backdropFade, BackdropLayer);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        // Animate card fade in
        var cardFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(cardFade, PaletteCard);
        Storyboard.SetTargetProperty(cardFade, "Opacity");

        // Animate card slide down
        var cardSlide = new DoubleAnimation
        {
            From = -12,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(cardSlide, PaletteTranslate);
        Storyboard.SetTargetProperty(cardSlide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(backdropFade);
        storyboard.Children.Add(cardFade);
        storyboard.Children.Add(cardSlide);
        storyboard.Begin();

        // Focus the search input after a brief delay so the control is ready
        DispatcherQueue.TryEnqueue(() =>
        {
            SearchInput.Focus(FocusState.Programmatic);
        });

        Log.Debug("Command palette opened");
    }

    /// <summary>
    /// Hides the command palette with a fade-out and slide-up animation.
    /// </summary>
    public void Hide()
    {
        if (!_isOpen) return;
        _isOpen = false;

        // Animate backdrop fade out
        var backdropFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs * 0.75)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(backdropFade, BackdropLayer);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        // Animate card fade out
        var cardFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs * 0.75)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(cardFade, PaletteCard);
        Storyboard.SetTargetProperty(cardFade, "Opacity");

        // Animate card slide up
        var cardSlide = new DoubleAnimation
        {
            From = 0,
            To = -12,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs * 0.75)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(cardSlide, PaletteTranslate);
        Storyboard.SetTargetProperty(cardSlide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(backdropFade);
        storyboard.Children.Add(cardFade);
        storyboard.Children.Add(cardSlide);

        storyboard.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
        };

        storyboard.Begin();

        Log.Debug("Command palette closed");
    }

    /// <summary>
    /// Toggles the command palette open or closed.
    /// </summary>
    public void Toggle()
    {
        if (_isOpen)
            Hide();
        else
            Show();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEARCH & FILTERING
    // ═══════════════════════════════════════════════════════════════════

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchInput.Text.Trim();
        FilterItems(query);
        _selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
        RenderResults();
    }

    /// <summary>
    /// Performs fuzzy matching: splits the query into words and checks whether
    /// all words appear (case-insensitive) in the item's Name or Description.
    /// </summary>
    private void FilterItems(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _filteredItems = new List<CommandItem>(_allItems);
            return;
        }

        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        _filteredItems = _allItems.Where(item =>
        {
            var searchable = $"{item.Name} {item.Description} {item.Category}".ToLowerInvariant();
            return words.All(word => searchable.Contains(word));
        })
        .OrderBy(item =>
        {
            // Prioritize items where the name starts with the query
            var nameLower = item.Name.ToLowerInvariant();
            var queryLower = query.ToLowerInvariant();
            if (nameLower.StartsWith(queryLower)) return 0;
            if (nameLower.Contains(queryLower)) return 1;
            return 2;
        })
        .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RESULTS RENDERING
    // ═══════════════════════════════════════════════════════════════════

    private void RenderResults()
    {
        ResultsPanel.Children.Clear();
        _renderedItemBorders.Clear();

        if (_filteredItems.Count == 0)
        {
            RenderEmptyState();
            return;
        }

        // Group items by category
        var groups = _filteredItems
            .GroupBy(i => i.Category)
            .OrderBy(g => GetCategorySortOrder(g.Key));

        int globalIndex = 0;

        foreach (var group in groups)
        {
            // Category header
            var header = new TextBlock
            {
                Text = group.Key.ToUpperInvariant(),
                FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextTertiaryBrush"],
                Padding = new Thickness(8, 10, 8, 4),
                CharacterSpacing = 80,
            };
            ResultsPanel.Children.Add(header);

            foreach (var item in group)
            {
                var itemBorder = CreateItemElement(item, globalIndex);
                ResultsPanel.Children.Add(itemBorder);
                _renderedItemBorders.Add(itemBorder);
                globalIndex++;
            }
        }

        UpdateSelectionVisuals();
    }

    private void RenderEmptyState()
    {
        var emptyPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 32, 0, 32),
            Spacing = 8,
        };

        emptyPanel.Children.Add(new FontIcon
        {
            Glyph = "\uE773",
            FontSize = 28,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextTertiaryBrush"],
        });

        emptyPanel.Children.Add(new TextBlock
        {
            Text = "No matching commands",
            FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextTertiaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        ResultsPanel.Children.Add(emptyPanel);
    }

    private Border CreateItemElement(CommandItem item, int index)
    {
        // Outer border (the selectable row)
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 1, 0, 1),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Tag = index,
        };

        // Content grid: [AccentBar] [Icon] [Text] [Shortcut]
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(3, GridUnitType.Pixel) },
                new ColumnDefinition { Width = new GridLength(36, GridUnitType.Pixel) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        // Accent left bar (visible only when selected)
        var accentBar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 196, 30, 58)),
            Margin = new Thickness(0, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = "AccentBar",
        };
        Grid.SetColumn(accentBar, 0);
        grid.Children.Add(accentBar);

        // Icon
        var icon = new FontIcon
        {
            Glyph = item.IconGlyph,
            FontSize = 16,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        // Text column
        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        textStack.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        textStack.Children.Add(new TextBlock
        {
            Text = item.Description,
            FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextTertiaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);

        // Keyboard shortcut hint
        if (!string.IsNullOrEmpty(item.ShortcutHint))
        {
            var shortcutBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 26)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            shortcutBorder.Child = new TextBlock
            {
                Text = item.ShortcutHint,
                FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextTertiaryBrush"],
            };

            Grid.SetColumn(shortcutBorder, 3);
            grid.Children.Add(shortcutBorder);
        }

        border.Child = grid;

        // Pointer events for hover/click
        border.PointerEntered += (s, e) =>
        {
            if (s is Border b && b.Tag is int idx)
            {
                _selectedIndex = idx;
                UpdateSelectionVisuals();
            }
        };

        border.PointerExited += (s, e) =>
        {
            // Keep the selection visual on pointer exit (keyboard can still move it)
        };

        border.Tapped += (s, e) =>
        {
            ExecuteSelected();
        };

        return border;
    }

    private void UpdateSelectionVisuals()
    {
        for (int i = 0; i < _renderedItemBorders.Count; i++)
        {
            var border = _renderedItemBorders[i];
            bool isSelected = (i == _selectedIndex);

            // Update row background
            border.Background = isSelected
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 26, 26))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

            // Update accent bar visibility
            if (border.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Border accentBar && accentBar.Tag is string tagStr && tagStr == "AccentBar")
                    {
                        accentBar.Background = isSelected
                            ? (SolidColorBrush)Application.Current.Resources["AccentPrimaryBrush"]
                            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 196, 30, 58));
                        break;
                    }
                }
            }
        }

        // Scroll selected item into view
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _renderedItemBorders.Count)
        {
            var selectedBorder = _renderedItemBorders[_selectedIndex];
            var transform = selectedBorder.TransformToVisual(ResultsScroller);
            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            // If the item is below the visible area, scroll down
            if (position.Y + selectedBorder.ActualHeight > ResultsScroller.ActualHeight)
            {
                ResultsScroller.ChangeView(null, ResultsScroller.VerticalOffset + position.Y + selectedBorder.ActualHeight - ResultsScroller.ActualHeight + 8, null);
            }
            // If the item is above the visible area, scroll up
            else if (position.Y < 0)
            {
                ResultsScroller.ChangeView(null, ResultsScroller.VerticalOffset + position.Y - 8, null);
            }
        }
    }

    private static int GetCategorySortOrder(string category) => category switch
    {
        "Pages" => 0,
        "Actions" => 1,
        "Documents" => 2,
        "Conversations" => 3,
        _ => 99,
    };

    // ═══════════════════════════════════════════════════════════════════
    //  KEYBOARD HANDLING
    // ═══════════════════════════════════════════════════════════════════

    private void SearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                e.Handled = true;
                MoveSelection(1);
                break;

            case Windows.System.VirtualKey.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;

            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                ExecuteSelected();
                break;

            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                Hide();
                break;

            case Windows.System.VirtualKey.Tab:
                // Tab moves selection down, Shift+Tab moves up
                e.Handled = true;
                var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool isShiftDown = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                MoveSelection(isShiftDown ? -1 : 1);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_filteredItems.Count == 0)
        {
            _selectedIndex = -1;
            return;
        }

        _selectedIndex += delta;

        // Wrap around
        if (_selectedIndex < 0)
            _selectedIndex = _filteredItems.Count - 1;
        else if (_selectedIndex >= _filteredItems.Count)
            _selectedIndex = 0;

        UpdateSelectionVisuals();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMMAND EXECUTION
    // ═══════════════════════════════════════════════════════════════════

    private void ExecuteSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _filteredItems.Count)
            return;

        var item = _filteredItems[_selectedIndex];
        Hide();

        switch (item.Kind)
        {
            case CommandItemKind.Page:
                Log.Information("Command palette navigating to page: {Page}", item.Target);
                NavigateToPageRequested?.Invoke(item.Target);
                break;

            case CommandItemKind.Action:
                Log.Information("Command palette executing action: {Action}", item.Target);
                ExecuteActionRequested?.Invoke(item.Target);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BACKDROP DISMISS
    // ═══════════════════════════════════════════════════════════════════

    private void BackdropLayer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        Hide();
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  DATA MODELS
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a single command item in the command palette.
/// </summary>
public sealed class CommandItem
{
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string IconGlyph { get; }
    public string Target { get; }
    public CommandItemKind Kind { get; }
    public string ShortcutHint { get; }

    public CommandItem(string name, string description, string category, string iconGlyph, string target, CommandItemKind kind, string shortcutHint)
    {
        Name = name;
        Description = description;
        Category = category;
        IconGlyph = iconGlyph;
        Target = target;
        Kind = kind;
        ShortcutHint = shortcutHint;
    }
}

/// <summary>
/// Defines the type of command: either a page navigation or a general action.
/// </summary>
public enum CommandItemKind
{
    /// <summary>Navigate to a specific page in the app.</summary>
    Page,

    /// <summary>Execute a non-navigation action (e.g., import files, toggle theme).</summary>
    Action,
}
