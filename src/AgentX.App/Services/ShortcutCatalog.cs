using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Shortcuts;

namespace AgentX.App.Services;

/// <param name="NavigateAsync">
/// Navigates to a page tag with an optional navigation parameter (an entity id or one
/// of <see cref="NavigationIntents"/>).
/// </param>
public sealed record ShortcutCatalogActions(
    Func<string, object?, CancellationToken, Task> NavigateAsync,
    Func<CancellationToken, Task> ShowCommandPaletteAsync,
    Func<CancellationToken, Task> ShowJumpToAsync,
    Func<CancellationToken, Task> ShowCheatsheetAsync);

/// <summary>
/// Seeds global keyboard shortcuts into the shared A2 registry. Their labels and categories are
/// what the Keyboard Shortcuts dialog lists, in the user's language.
/// </summary>
public sealed class ShortcutCatalog
{
    private readonly IShortcutRegistry _registry;
    private readonly ILocalizationService _localization;
    private bool _seeded;

    /// <summary>
    /// The registry id whose primary chord is the canonical keyboard route to a page.
    /// Surfaces that print a hint next to a page name (the command palette) read the
    /// chord from the registry through this map, so a remapped or removed shortcut
    /// can never leave a stale hint behind.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PageShortcutIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dashboard"] = "nav.dashboard",
            ["Chat"] = "nav.page2",
            ["AskFiles"] = "nav.page3",
            ["Search"] = "nav.search",
            ["KnowledgeVault"] = "nav.vault",
            ["Collections"] = "nav.page6",
            ["Workflows"] = "nav.workflows",
            ["ModelManager"] = "nav.page8",
            ["Settings"] = "nav.settings",
            ["Analytics"] = "nav.analytics",
            ["Operations"] = "nav.operations",
            ["WebImport"] = "nav.webimport",
            ["KnowledgeGraph"] = "nav.graph",
        };

    /// <summary>Same idea as <see cref="PageShortcutIds"/>, for palette actions.</summary>
    private static readonly IReadOnlyDictionary<string, string> ActionShortcutIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NewConversation"] = "nav.chat",
        };

    public ShortcutCatalog(IShortcutRegistry registry, ILocalizationService localization)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    public void SeedDefaults(ShortcutCatalogActions actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (_seeded) return;

        _seeded = true;

        var navigation = _localization.GetString("Shortcut_CategoryNavigation");
        var commandPalette = _localization.GetString("Shortcut_CommandPalette");
        var semanticSearch = _localization.GetString("Shortcut_SemanticSearch");

        Global("cmd.palette", commandPalette, KeyModifiers.Ctrl, VirtualKeyCode.K, actions.ShowCommandPaletteAsync, navigation);
        Global("cmd.palette.alt", commandPalette, KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, actions.ShowCommandPaletteAsync, navigation);

        // Labelled "New Conversation", so it starts one rather than landing on whatever
        // thread was last open.
        Global("nav.chat", _localization.GetString("Shortcut_NewConversation"), KeyModifiers.Ctrl, VirtualKeyCode.N, Navigate(actions, "Chat", NavigationIntents.NewConversation), navigation);
        Global("nav.vault", _localization.GetString("Shortcut_KnowledgeVault"), KeyModifiers.Ctrl, VirtualKeyCode.I, Navigate(actions, "KnowledgeVault"), navigation);
        Global("nav.search", semanticSearch, KeyModifiers.Ctrl, VirtualKeyCode.F, Navigate(actions, "Search"), navigation);
        Global("nav.search.alt", semanticSearch, KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.F, Navigate(actions, "Search"), navigation);
        Global("nav.settings", _localization.GetString("Shortcut_Settings"), KeyModifiers.Ctrl, VirtualKeyCode.OemComma, Navigate(actions, "Settings"), navigation);
        Global("nav.analytics", _localization.GetString("Shortcut_Analytics"), KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.A, Navigate(actions, "Analytics"), navigation);
        Global("nav.operations", _localization.GetString("Shortcut_Operations"), KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.O, Navigate(actions, "Operations"), navigation);

        var pageOrder = new[]
        {
            "Dashboard",
            "Chat",
            "AskFiles",
            "Search",
            "KnowledgeVault",
            "Collections",
            "Workflows",
            "ModelManager",
            "Settings"
        };
        var quickAccess = _localization.GetString("Shortcut_CategoryQuickAccess");
        for (var i = 0; i < pageOrder.Length; i++)
        {
            var pageTag = pageOrder[i];
            var key = VirtualKeyCode.D1 + i;
            Global(
                $"nav.page{i + 1}",
                _localization.GetString("Shortcut_QuickAccessPage", PageName(pageTag), i + 1),
                KeyModifiers.Ctrl,
                key,
                Navigate(actions, pageTag),
                quickAccess);
        }

        var pageActions = _localization.GetString("Shortcut_CategoryActions");
        Global("nav.workflows", _localization.GetString("Shortcut_Workflows"), KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.W, Navigate(actions, "Workflows"), pageActions);
        Global("nav.webimport", _localization.GetString("Shortcut_WebImport"), KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.E, Navigate(actions, "WebImport"), pageActions);
        Global("nav.dashboard", _localization.GetString("Shortcut_Dashboard"), KeyModifiers.Ctrl, VirtualKeyCode.D, Navigate(actions, "Dashboard"), pageActions);
        Global("nav.graph", _localization.GetString("Shortcut_KnowledgeGraph"), KeyModifiers.Ctrl, VirtualKeyCode.G, Navigate(actions, "KnowledgeGraph"), pageActions);

        var help = _localization.GetString("Shortcut_CategoryHelp");
        Global("help.shortcuts", _localization.GetString("Shortcut_ShowKeyboardShortcuts"), KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.Oem2, actions.ShowCheatsheetAsync, help);
        Global("help.cheatsheet", _localization.GetString("Shortcut_KeyboardShortcuts"), KeyModifiers.None, VirtualKeyCode.F1, actions.ShowCheatsheetAsync, help);
        Global("help.jump", _localization.GetString("Shortcut_JumpTo"), KeyModifiers.Ctrl, VirtualKeyCode.P, actions.ShowJumpToAsync, help);
    }

    /// <summary>
    /// The name the rail gives a page, for the Ctrl+number shortcuts. Their labels used to show
    /// the page's internal tag ("AskFiles", "ModelManager").
    /// </summary>
    private string PageName(string pageTag) => pageTag switch
    {
        "Dashboard" => _localization.GetString("Shortcut_Dashboard"),
        "Chat" => _localization.GetString("Shortcut_PageChat"),
        "AskFiles" => _localization.GetString("Shortcut_PageAskFiles"),
        "Search" => _localization.GetString("Shortcut_SemanticSearch"),
        "KnowledgeVault" => _localization.GetString("Shortcut_KnowledgeVault"),
        "Collections" => _localization.GetString("Shortcut_PageCollections"),
        "Workflows" => _localization.GetString("Shortcut_Workflows"),
        "ModelManager" => _localization.GetString("Shortcut_PageModelManager"),
        "Settings" => _localization.GetString("Shortcut_Settings"),
        _ => pageTag
    };

    /// <summary>
    /// Display chord for the canonical shortcut that opens <paramref name="pageTag"/>,
    /// or null when the page has none. Read from the live registry, never from a literal.
    /// </summary>
    public string? PageChordDisplay(string pageTag) =>
        PageShortcutIds.TryGetValue(pageTag, out var id) ? ChordDisplay(id) : null;

    /// <summary>Display chord for a palette action id, or null when it has none.</summary>
    public string? ActionChordDisplay(string actionId) =>
        ActionShortcutIds.TryGetValue(actionId, out var id) ? ChordDisplay(id) : null;

    private string? ChordDisplay(string shortcutId) =>
        _registry.All().FirstOrDefault(d => d.Id == shortcutId)?.PrimaryKey.Display;

    private static Func<CancellationToken, Task> Navigate(
        ShortcutCatalogActions actions, string pageTag, object? parameter = null)
        => ct => actions.NavigateAsync(pageTag, parameter, ct);

    private void Global(
        string id,
        string label,
        KeyModifiers modifiers,
        VirtualKeyCode key,
        Func<CancellationToken, Task> handler,
        string category)
    {
        _registry.Register(new AgentX.Core.Services.Shortcuts.ShortcutDescriptor(
            id,
            label,
            ShortcutScope.Global,
            new[] { new KeyChord(modifiers, key) },
            handler,
            category));
    }
}
