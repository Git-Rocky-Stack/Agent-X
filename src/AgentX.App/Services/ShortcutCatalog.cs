using System;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;

namespace AgentX.App.Services;

public sealed record ShortcutCatalogActions(
    Func<string, CancellationToken, Task> NavigateAsync,
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

        Global("nav.chat", "New Conversation", KeyModifiers.Ctrl, VirtualKeyCode.N, Navigate(actions, "Chat"), "Navigation");
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

    private static Func<CancellationToken, Task> Navigate(ShortcutCatalogActions actions, string pageTag)
        => ct => actions.NavigateAsync(pageTag, ct);

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
