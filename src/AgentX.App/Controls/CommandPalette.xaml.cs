using System;
using System.Collections.Generic;
using System.Linq;
using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace AgentX.App.Controls;

/// <summary>
/// One page as the navigation rail registered it: the same tag, localized label,
/// group placard and icon glyph the rail shows, plus the display chord of its
/// keyboard shortcut when it has one.
/// </summary>
public sealed record CommandPalettePage(
    string Tag,
    string Label,
    string Group,
    int GroupOrder,
    string Glyph,
    string? ShortcutHint);

/// <summary>
/// The Ctrl+K command palette overlay.
/// <para>
/// Pages are not listed here. The shell registers them from the navigation rail
/// through <see cref="Configure"/>, so the palette shows exactly the rail's pages,
/// under the rail's localized names, icons and group placards, and a page added to
/// the rail appears here without anyone remembering to add it. Three actions and the
/// shortcuts scoped to the current page (via <see cref="CommandPaletteViewModel"/>)
/// follow the pages.
/// </para>
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────
    private const double AnimationDurationMs = 200;

    /// <summary>Rail groups sort by their rail order; these two follow them.</summary>
    private const int ActionsGroupOrder = 900;
    private const int OnThisPageGroupOrder = 950;

    private const string KeyboardGlyph = "";

    // ── State ─────────────────────────────────────────────────────────
    private bool _isOpen;
    private int _selectedIndex = -1;
    private IReadOnlyList<CommandPalettePage> _pages = Array.Empty<CommandPalettePage>();
    private Func<string, string?> _actionShortcutHint = _ => null;
    private Func<string?> _activeScopeName = () => null;
    private ILocalizationService? _localization;
    private CommandPaletteViewModel? _scopedShortcuts;
    private List<CommandItem> _allItems = new();
    private List<CommandItem> _filteredItems = new();
    private readonly List<Border> _renderedItemBorders = new();

    // ── Callbacks ─────────────────────────────────────────────────────
    /// <summary>
    /// Delegate invoked when the user selects a page. The string parameter is the
    /// page tag (e.g., "Dashboard", "Chat").
    /// </summary>
    public Action<string>? NavigateToPageRequested { get; set; }

    /// <summary>
    /// Delegate invoked when the user selects an action. The string parameter is the
    /// action identifier (e.g., "NewConversation").
    /// </summary>
    public Action<string>? ExecuteActionRequested { get; set; }

    /// <summary>
    /// Gets whether the command palette is currently visible.
    /// </summary>
    public bool IsOpen => _isOpen;

    public CommandPalette()
    {
        InitializeComponent();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  REGISTRATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hands the palette the rail's pages and the two lookups it cannot own itself.
    /// </summary>
    /// <param name="pages">The navigation rail's pages, in rail order.</param>
    /// <param name="actionShortcutHint">
    /// Display chord for an action id, or null. Read from the shortcut registry so a
    /// remapped chord never leaves a stale hint behind.
    /// </param>
    /// <param name="activeScopeName">
    /// The page currently in the content frame, for the "On This Page" group.
    /// </param>
    public void Configure(
        IReadOnlyList<CommandPalettePage> pages,
        Func<string, string?> actionShortcutHint,
        Func<string?> activeScopeName)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _actionShortcutHint = actionShortcutHint ?? throw new ArgumentNullException(nameof(actionShortcutHint));
        _activeScopeName = activeScopeName ?? throw new ArgumentNullException(nameof(activeScopeName));
    }

    /// <summary>
    /// Localization is resolved lazily: the palette is built with the window, before
    /// the app's localization service has initialized, and every string it renders in
    /// code is fetched at open time.
    /// </summary>
    private ILocalizationService Localization =>
        _localization ??= App.GetService<ILocalizationService>();

    private CommandPaletteViewModel ScopedShortcuts =>
        _scopedShortcuts ??= App.GetService<CommandPaletteViewModel>();

    /// <summary>
    /// Builds the item list for this opening: rail pages, then actions, then the
    /// shortcuts the current page registered.
    /// </summary>
    private void BuildItems()
    {
        var items = new List<CommandItem>(_pages.Count + 8);

        foreach (var page in _pages)
        {
            items.Add(new CommandItem(
                page.Label, page.Group, page.GroupOrder, page.Glyph, page.Tag,
                CommandItemKind.Page, page.ShortcutHint, null));
        }

        // Keys are literal at the call site so the LocaleAudit extractor sees them.
        var actions = Localization.GetString("Palette_Actions");
        items.Add(Action(actions, "NewConversation", Localization.GetString("Palette_NewConversation"), ""));
        items.Add(Action(actions, "ImportFiles", Localization.GetString("Palette_ImportFiles"), ""));
        items.Add(Action(actions, "ToggleTheme", Localization.GetString("Palette_ToggleTheme"), ""));

        var scope = _activeScopeName();
        if (!string.IsNullOrEmpty(scope))
        {
            var scoped = ScopedShortcuts;
            scoped.Query = string.Empty;
            scoped.ActiveScopeName = scope;

            var header = Localization.GetString("Palette_OnThisPage");
            foreach (var descriptor in scoped.Results.Where(d => !d.Scope.IsGlobal))
            {
                items.Add(new CommandItem(
                    descriptor.Label, header, OnThisPageGroupOrder, KeyboardGlyph, descriptor.Id,
                    CommandItemKind.ScopedShortcut, descriptor.DisplayChord, descriptor));
            }
        }

        _allItems = items;
    }

    private CommandItem Action(string group, string id, string label, string glyph) =>
        new(label, group, ActionsGroupOrder, glyph, id,
            CommandItemKind.Action, _actionShortcutHint(id), null);

    // ═══════════════════════════════════════════════════════════════════
    //  SHOW / HIDE WITH ANIMATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the command palette with a fade-in and slide-down animation.
    /// Rebuilds the items and resets the search text and selection state.
    /// </summary>
    public void Show()
    {
        if (_isOpen) return;
        _isOpen = true;

        BuildItems();
        SearchInput.Text = string.Empty;
        FilterItems(string.Empty);
        _selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
        RenderResults();

        // Make visible before animating
        Visibility = Visibility.Visible;

        var backdropFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(backdropFade, BackdropLayer);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        var cardFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(cardFade, PaletteCard);
        Storyboard.SetTargetProperty(cardFade, "Opacity");

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

        Log.Debug("Command palette opened with {Count} items", _allItems.Count);
    }

    /// <summary>
    /// Hides the command palette with a fade-out and slide-up animation.
    /// </summary>
    public void Hide()
    {
        if (!_isOpen) return;
        _isOpen = false;

        var backdropFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs * 0.75)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(backdropFade, BackdropLayer);
        Storyboard.SetTargetProperty(backdropFade, "Opacity");

        var cardFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs * 0.75)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(cardFade, PaletteCard);
        Storyboard.SetTargetProperty(cardFade, "Opacity");

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
        FilterItems(SearchInput.Text.Trim());
        _selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
        RenderResults();
    }

    /// <summary>
    /// Ranks items with the same subsequence matcher the shortcut registry uses, then
    /// orders by group so the rendered rows and the selection index always agree.
    /// (The previous implementation ranked one way and rendered another, so Enter could
    /// run a different row than the highlighted one.)
    /// </summary>
    private void FilterItems(string query)
    {
        IEnumerable<CommandItem> matches = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : FuzzyMatcher.Rank(_allItems, item => item.Name, query).Select(s => s.Item);

        // OrderBy is stable: rank order survives inside each group.
        _filteredItems = matches.OrderBy(item => item.GroupOrder).ToList();
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

        string? currentGroup = null;
        for (var index = 0; index < _filteredItems.Count; index++)
        {
            var item = _filteredItems[index];
            if (!string.Equals(item.Category, currentGroup, StringComparison.Ordinal))
            {
                currentGroup = item.Category;
                ResultsPanel.Children.Add(CreateGroupPlacard(item.Category));
            }

            var row = CreateItemElement(item, index);
            ResultsPanel.Children.Add(row);
            _renderedItemBorders.Add(row);
        }

        UpdateSelectionVisuals();
    }

    /// <summary>
    /// Group placard: the same Archivo stencil the rail's group headers wear
    /// (Navigation.xaml NavHeaderStyle), so the two surfaces read as one instrument.
    /// </summary>
    private static TextBlock CreateGroupPlacard(string text) => new()
    {
        Text = text,
        FontFamily = (FontFamily)Application.Current.Resources["FontDisplayBold"],
        FontSize = 10,
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        CharacterSpacing = 100,
        Foreground = ThemeResources.Brush("TextTertiaryBrush"),
        Padding = new Thickness(8, 8, 8, 4),
    };

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
            Glyph = "",
            FontSize = 28,
            Foreground = ThemeResources.Brush("TextTertiaryBrush"),
        });

        emptyPanel.Children.Add(new TextBlock
        {
            Text = Localization.GetString("Palette_NoMatches"),
            FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
            FontSize = 14,
            Foreground = ThemeResources.Brush("TextTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        ResultsPanel.Children.Add(emptyPanel);
    }

    /// <summary>
    /// Radius tokens from the machined scale, never literals. A miss throws instead of
    /// falling back, because 0 is itself a radius on that scale: a silent fallback renders
    /// the wrong shape where neither the compiler nor MachinedRadiusStopsTests can see it.
    /// A missing StaticResource fails the same way in XAML.
    /// </summary>
    private static CornerRadius Radius(string token) =>
        ThemeResources.Get(token) is CornerRadius radius
            ? radius
            : throw new KeyNotFoundException(
                $"Radius token '{token}' is not defined in the style dictionaries.");

    private Border CreateItemElement(CommandItem item, int index)
    {
        // Outer border (the selectable row): a raised control, so RControl
        var border = new Border
        {
            CornerRadius = Radius("RControl"),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Background = UnlitBrush(),
            Tag = index,
        };

        // Content grid: [AccentBar] [Icon] [Name] [Shortcut]
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
            CornerRadius = Radius("RCard"),
            Background = UnlitBrush(),
            Margin = new Thickness(0, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = "AccentBar",
        };
        Grid.SetColumn(accentBar, 0);
        grid.Children.Add(accentBar);

        // Icon: the same glyph the rail shows for the page
        var icon = new FontIcon
        {
            Glyph = item.IconGlyph,
            FontSize = 16,
            Foreground = ThemeResources.Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var name = new TextBlock
        {
            Text = item.Name,
            FontFamily = (FontFamily)Application.Current.Resources["FontPrimary"],
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Foreground = ThemeResources.Brush("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);

        // Keyboard chord hint: a well, void-black in both shifts, like the sibling
        // chord hints in CommandPalette.xaml.
        if (!string.IsNullOrEmpty(item.ShortcutHint))
        {
            var shortcutBorder = new Border
            {
                Background = ThemeResources.Brush("VoidBrush"),
                BorderBrush = ThemeResources.Brush("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = Radius("RCard"),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            shortcutBorder.Child = new TextBlock
            {
                Text = item.ShortcutHint,
                FontFamily = (FontFamily)Application.Current.Resources["FontTelemetry"],
                FontSize = 10,
                Foreground = ThemeResources.Brush("WellTextBrush"),
                Opacity = 0.5,
            };

            Grid.SetColumn(shortcutBorder, 3);
            grid.Children.Add(shortcutBorder);
        }

        border.Child = grid;

        border.PointerEntered += (s, _) =>
        {
            if (s is Border b && b.Tag is int idx)
            {
                _selectedIndex = idx;
                UpdateSelectionVisuals();
            }
        };

        border.Tapped += (_, _) => ExecuteSelected();

        return border;
    }

    /// <summary>
    /// A fully transparent fill for unlit surfaces (unselected rows, the dark
    /// accent bar). Transparent rather than null so the row still hit-tests for
    /// the pointer handlers.
    /// </summary>
    private static SolidColorBrush UnlitBrush() =>
        new(Microsoft.UI.Colors.Transparent);

    private void UpdateSelectionVisuals()
    {
        for (int i = 0; i < _renderedItemBorders.Count; i++)
        {
            var border = _renderedItemBorders[i];
            bool isSelected = (i == _selectedIndex);

            // The selected row rides the shift-following selection surface; an
            // unselected row stays transparent but must remain hit-testable.
            border.Background = isSelected
                ? ThemeResources.Brush("CardHoverBrush")
                : UnlitBrush();

            if (border.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Border accentBar && accentBar.Tag is string tagStr && tagStr == "AccentBar")
                    {
                        accentBar.Background = isSelected
                            ? ThemeResources.Brush("AccentPrimaryBrush")
                            : UnlitBrush();
                        break;
                    }
                }
            }
        }

        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _renderedItemBorders.Count)
        {
            var selectedBorder = _renderedItemBorders[_selectedIndex];
            var transform = selectedBorder.TransformToVisual(ResultsScroller);
            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            if (position.Y + selectedBorder.ActualHeight > ResultsScroller.ActualHeight)
            {
                ResultsScroller.ChangeView(null, ResultsScroller.VerticalOffset + position.Y + selectedBorder.ActualHeight - ResultsScroller.ActualHeight + 8, null);
            }
            else if (position.Y < 0)
            {
                ResultsScroller.ChangeView(null, ResultsScroller.VerticalOffset + position.Y - 8, null);
            }
        }
    }

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

            case CommandItemKind.ScopedShortcut when item.Descriptor is not null:
                Log.Information("Command palette running page shortcut: {Shortcut}", item.Target);
                _ = ScopedShortcuts.ExecuteAsync(item.Descriptor);
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
/// One row in the palette. <see cref="Category"/> is the group placard text and
/// <see cref="GroupOrder"/> its position; both come from the rail for pages.
/// </summary>
public sealed record CommandItem(
    string Name,
    string Category,
    int GroupOrder,
    string IconGlyph,
    string Target,
    CommandItemKind Kind,
    string? ShortcutHint,
    ShortcutDescriptor? Descriptor);

/// <summary>
/// Defines what selecting a row does.
/// </summary>
public enum CommandItemKind
{
    /// <summary>Navigate to a page the rail registered.</summary>
    Page,

    /// <summary>Execute a palette action (new conversation, import files, toggle theme).</summary>
    Action,

    /// <summary>Run a shortcut the current page registered with the shortcut registry.</summary>
    ScopedShortcut,
}
