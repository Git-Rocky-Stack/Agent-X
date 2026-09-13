using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// Seeds global keyboard shortcuts into the shared A2 registry.
/// </summary>
public sealed class ShortcutCatalog
{
    private readonly IShortcutRegistry _registry;
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

    public ShortcutCatalog(IShortcutRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void SeedDefaults(ShortcutCatalogActions actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (_seeded) return;

        _seeded = true;

        Global("cmd.palette", "Command Palette", KeyModifiers.Ctrl, VirtualKeyCode.K, actions.ShowCommandPaletteAsync, "Navigation");
        Global("cmd.palette.alt", "Command Palette", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, actions.ShowCommandPaletteAsync, "Navigation");

        // Labelled "New Conversation", so it starts one rather than landing on whatever
        // thread was last open.
        Global("nav.chat", "New Conversation", KeyModifiers.Ctrl, VirtualKeyCode.N, Navigate(actions, "Chat", NavigationIntents.NewConversation), "Navigation");
        Global("nav.vault", "Knowledge Vault", KeyModifiers.Ctrl, VirtualKeyCode.I, Navigate(actions, "KnowledgeVault"), "Navigation");
        Global("nav.search", "Semantic Search", KeyModifiers.Ctrl, VirtualKeyCode.F, Navigate(actions, "Search"), "Navigation");
        Global("nav.search.alt", "Semantic Search", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.F, Navigate(actions, "Search"), "Navigation");
        Global("nav.settings", "Settings", KeyModifiers.Ctrl, VirtualKeyCode.OemComma, Navigate(actions, "Settings"), "Navigation");
        Global("nav.analytics", "Analytics", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.A, Navigate(actions, "Analytics"), "Navigation");
        Global("nav.operations", "Operations", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.O, Navigate(actions, "Operations"), "Navigation");

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
        for (var i = 0; i < pageOrder.Length; i++)
        {
            var pageTag = pageOrder[i];
            var key = VirtualKeyCode.D1 + i;
            Global(
                $"nav.page{i + 1}",
                $"{pageTag} (Ctrl+{i + 1})",
                KeyModifiers.Ctrl,
                key,
                Navigate(actions, pageTag),
                "Quick Access");
        }

        Global("nav.workflows", "Workflows", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.W, Navigate(actions, "Workflows"), "Actions");
        Global("nav.webimport", "Web Import", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.E, Navigate(actions, "WebImport"), "Actions");
        Global("nav.dashboard", "Dashboard", KeyModifiers.Ctrl, VirtualKeyCode.D, Navigate(actions, "Dashboard"), "Actions");
        Global("nav.graph", "Knowledge Graph", KeyModifiers.Ctrl, VirtualKeyCode.G, Navigate(actions, "KnowledgeGraph"), "Actions");

        Global("help.shortcuts", "Show Keyboard Shortcuts", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.Oem2, actions.ShowCheatsheetAsync, "Help");
        Global("help.cheatsheet", "Keyboard Shortcuts", KeyModifiers.None, VirtualKeyCode.F1, actions.ShowCheatsheetAsync, "Help");
        Global("help.jump", "Jump To", KeyModifiers.Ctrl, VirtualKeyCode.P, actions.ShowJumpToAsync, "Help");
    }

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
