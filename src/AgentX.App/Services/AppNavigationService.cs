using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using AgentX.Core.Services.Settings;

namespace AgentX.App.Services;

/// <summary>
/// Orchestrates page navigation within the main window. Keeps the content frame
/// and NavigationView selection in sync. Exposes navigation for command palette,
/// keyboard shortcuts, and tray menu actions.
/// </summary>
public sealed class AppNavigationService : IAppNavigationService
{
    private Dictionary<string, Type> _pageMap = new();
    private Dictionary<string, NavigationViewItem> _navItemMap = new();
    private Frame _contentFrame = null!;
    private NavigationView _navView = null!;
    private bool _initialized;

    /// <inheritdoc />
    public bool SuppressNavigation { get; set; }

    /// <inheritdoc />
    public string? CurrentPage { get; private set; }

    /// <inheritdoc />
    public event EventHandler<string>? PageChanged;

    /// <inheritdoc />
    public void Initialize(
        Dictionary<string, Type> pageMap,
        Dictionary<string, NavigationViewItem> navItemMap,
        Frame contentFrame,
        NavigationView navView)
    {
        ArgumentNullException.ThrowIfNull(pageMap);
        ArgumentNullException.ThrowIfNull(navItemMap);
        ArgumentNullException.ThrowIfNull(contentFrame);
        ArgumentNullException.ThrowIfNull(navView);

        _pageMap = pageMap;
        _navItemMap = navItemMap;
        _contentFrame = contentFrame;
        _navView = navView;

        // Wire the NavigationView selection changed event
        _navView.SelectionChanged += OnSelectionChanged;

        _initialized = true;
        Log.Debug("AppNavigationService initialized with {Count} pages", pageMap.Count);
    }

    /// <inheritdoc />
    public void NavigateToPage(string pageKey)
    {
        ArgumentNullException.ThrowIfNull(pageKey);

        if (!_initialized)
        {
            Log.Warning("NavigationService not initialized; cannot navigate to {Page}", pageKey);
            return;
        }

        if (_pageMap.TryGetValue(pageKey, out var pageType))
        {
            _contentFrame.Navigate(pageType);

            // Sync the NavigationView selection to reflect the new page
            if (_navItemMap.TryGetValue(pageKey, out var navItem))
            {
                var wasSuppressingNavigation = SuppressNavigation;
                SuppressNavigation = true;
                try
                {
                    _navView.SelectedItem = navItem;
                }
                finally
                {
                    SuppressNavigation = wasSuppressingNavigation;
                }
            }

            CurrentPage = pageKey;
            PageChanged?.Invoke(this, pageKey);
            Log.Debug("Navigated to {Page} via service", pageKey);
        }
        else
        {
            Log.Debug("Attempted to navigate to unknown page: {Page}", pageKey);
        }
    }

    /// <inheritdoc />
    public void EnsureNavPaneVisible()
    {
        if (!_initialized) return;

        _navView.IsPaneVisible = true;
        _navView.IsPaneOpen = true;
    }

    /// <inheritdoc />
    public void ExecuteAction(string actionId)
    {
        switch (actionId)
        {
            case "NewConversation":
                NavigateToPage("Chat");
                break;

            case "ImportFiles":
                NavigateToPage("KnowledgeVault");
                break;

            case "RefreshDashboard":
                NavigateToPage("Dashboard");
                break;

            case "ToggleTheme":
                try
                {
                    var themeService = App.GetService<IThemeService>();
                    var newTheme = themeService.CurrentTheme == Microsoft.UI.Xaml.ElementTheme.Dark
                        ? Microsoft.UI.Xaml.ElementTheme.Light
                        : Microsoft.UI.Xaml.ElementTheme.Dark;
                    _ = themeService.SetThemeAsync(newTheme);
                    Log.Information("Theme toggled to {Theme} via command palette", newTheme);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to toggle theme");
                }
                break;

            default:
                Log.Warning("Unknown command palette action: {Action}", actionId);
                break;
        }
    }

    // ── Private ──────────────────────────────────────────────────

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Guard: don't navigate when onboarding setup is modifying NavView state
        if (SuppressNavigation) return;

        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (tag != null && _pageMap.TryGetValue(tag, out var pageType))
            {
                _contentFrame.Navigate(pageType);
                CurrentPage = tag;
                PageChanged?.Invoke(this, tag);
                Log.Debug("Navigated to {Page} via NavView selection", tag);
            }
            else if (tag != null)
            {
                Log.Debug("Page not yet implemented: {Page}", tag);
            }
        }
    }
}
