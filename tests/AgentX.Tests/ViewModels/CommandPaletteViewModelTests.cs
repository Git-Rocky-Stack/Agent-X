using System.Linq;
using System.Threading.Tasks;
using AgentX.App.Services;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    [Fact]
    public void Initial_state_lists_all_global_and_active_scope_descriptors()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Desc("g.one", "Global one", ShortcutScope.Global));
        registry.Register(Desc("d.one", "Docs one", new ShortcutScope("DocumentsPage")));
        registry.Register(Desc("c.one", "Chat one", new ShortcutScope("ChatPage")));

        var sut = new CommandPaletteViewModel(registry, activeScopeName: "DocumentsPage");

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("g.one", "d.one");
    }

    [Fact]
    public void Filter_narrows_results_fuzzy_by_label()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Desc("imp", "Import Document", ShortcutScope.Global));
        registry.Register(Desc("exp", "Export Document", ShortcutScope.Global));
        registry.Register(Desc("set", "Settings", ShortcutScope.Global));

        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);
        sut.Query = "doc";

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("imp", "exp");
    }

    [Fact]
    public async Task Execute_invokes_descriptor_handler()
    {
        var handlerFired = false;
        var descriptor = new ShortcutDescriptor(
            "x", "X action", ShortcutScope.Global,
            new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.X) },
            _ => { handlerFired = true; return Task.CompletedTask; });

        var registry = new ShortcutRegistry();
        registry.Register(descriptor);
        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);

        await sut.ExecuteAsync(descriptor);

        handlerFired.Should().BeTrue();
    }

    [Fact]
    public void Results_refresh_when_registry_changes()
    {
        var registry = new ShortcutRegistry();
        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);

        sut.Results.Should().BeEmpty();

        registry.Register(Desc("new", "New", ShortcutScope.Global));

        sut.Results.Should().ContainSingle(r => r.Id == "new");
    }

    [Fact]
    public void Seeded_global_navigation_entries_include_analytics_in_command_palette_results()
    {
        var registry = new ShortcutRegistry();
        var catalog = new ShortcutCatalog(registry);
        catalog.SeedDefaults(new ShortcutCatalogActions(
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask));

        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);

        sut.Results.Should().Contain(result => result.Id == "nav.analytics" && result.Label == "Analytics");
        sut.Results.Should().Contain(result => result.Id == "nav.operations" && result.Label == "Operations");
        sut.Results.Should().Contain(result => result.Id == "nav.dashboard");
        sut.Results.Should().Contain(result => result.Id == "nav.workflows");
    }

    private static ShortcutDescriptor Desc(string id, string label, ShortcutScope scope)
        => new(id, label, scope,
               new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.A) },
               _ => Task.CompletedTask);
}
