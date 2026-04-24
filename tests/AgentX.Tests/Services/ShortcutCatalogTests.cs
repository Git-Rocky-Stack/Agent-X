using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.App.Services;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

public class ShortcutCatalogTests
{
    [Fact]
    public void SeedDefaults_registers_global_shortcuts_from_legacy_catalog()
    {
        var registry = new ShortcutRegistry();
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions());

        var ids = registry.All().Select(s => s.Id);
        ids.Should().Contain(new[]
        {
            "cmd.palette",
            "nav.chat",
            "nav.vault",
            "nav.search",
            "nav.settings",
            "nav.analytics",
            "nav.operations",
            "nav.page1",
            "nav.page9",
            "nav.workflows",
            "nav.webimport",
            "nav.dashboard",
            "nav.graph",
            "help.shortcuts",
            "help.cheatsheet",
            "help.jump",
        });
    }

    [Fact]
    public async Task SeedDefaults_navigation_shortcut_invokes_configured_navigation_action()
    {
        var registry = new ShortcutRegistry();
        var navigatedTo = string.Empty;
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions(navigateAsync: (page, _) =>
        {
            navigatedTo = page;
            return Task.CompletedTask;
        }));

        var shortcut = registry.FindByPrimaryKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.N), null);
        shortcut.Should().NotBeNull();

        await shortcut!.Handler(CancellationToken.None);

        navigatedTo.Should().Be("Chat");
    }

    [Fact]
    public async Task SeedDefaults_help_shortcuts_invokes_configured_cheatsheet_action()
    {
        var registry = new ShortcutRegistry();
        var cheatsheetCalls = 0;
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions(showCheatsheetAsync: _ =>
        {
            cheatsheetCalls++;
            return Task.CompletedTask;
        }));

        var shortcut = registry.FindByPrimaryKey(
            new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.Oem2),
            null);

        shortcut.Should().NotBeNull();
        await shortcut!.Handler(CancellationToken.None);

        cheatsheetCalls.Should().Be(1);
    }

    [Fact]
    public async Task SeedDefaults_analytics_shortcut_invokes_configured_navigation_action()
    {
        var registry = new ShortcutRegistry();
        var navigatedTo = string.Empty;
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions(navigateAsync: (page, _) =>
        {
            navigatedTo = page;
            return Task.CompletedTask;
        }));

        var shortcut = registry.FindByPrimaryKey(
            new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.A),
            null);

        shortcut.Should().NotBeNull();
        await shortcut!.Handler(CancellationToken.None);

        navigatedTo.Should().Be("Analytics");
    }

    [Fact]
    public async Task SeedDefaults_operations_shortcut_invokes_configured_navigation_action()
    {
        var registry = new ShortcutRegistry();
        var navigatedTo = string.Empty;
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions(navigateAsync: (page, _) =>
        {
            navigatedTo = page;
            return Task.CompletedTask;
        }));

        var shortcut = registry.FindByPrimaryKey(
            new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.O),
            null);

        shortcut.Should().NotBeNull();
        await shortcut!.Handler(CancellationToken.None);

        navigatedTo.Should().Be("Operations");
    }

    [Fact]
    public void SeedDefaults_is_idempotent()
    {
        var registry = new ShortcutRegistry();
        var catalog = new ShortcutCatalog(registry);

        catalog.SeedDefaults(NoopActions());
        catalog.SeedDefaults(NoopActions());

        registry.All().Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    private static ShortcutCatalogActions NoopActions(
        Func<string, CancellationToken, Task>? navigateAsync = null,
        Func<CancellationToken, Task>? showPaletteAsync = null,
        Func<CancellationToken, Task>? showJumpToAsync = null,
        Func<CancellationToken, Task>? showCheatsheetAsync = null)
    {
        return new ShortcutCatalogActions(
            navigateAsync ?? ((_, _) => Task.CompletedTask),
            showPaletteAsync ?? (_ => Task.CompletedTask),
            showJumpToAsync ?? (_ => Task.CompletedTask),
            showCheatsheetAsync ?? (_ => Task.CompletedTask));
    }
}
