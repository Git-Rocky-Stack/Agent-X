using System.Linq;
using System.Threading.Tasks;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class CheatsheetViewModelTests
{
    [Fact]
    public void Groups_global_and_active_scope_shortcuts_by_category()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Descriptor("settings.open", "Open Settings", ShortcutScope.Global, VirtualKeyCode.OemComma, KeyModifiers.Ctrl, "Global"));
        registry.Register(Descriptor("docs.refresh", "Refresh Documents", new ShortcutScope("DocumentsPage"), VirtualKeyCode.F5, KeyModifiers.None, "Documents"));
        registry.Register(Descriptor("chat.new", "New Chat", new ShortcutScope("ChatPage"), VirtualKeyCode.N, KeyModifiers.Ctrl, "Chat"));

        var sut = new CheatsheetViewModel(registry, "DocumentsPage");

        sut.Groups.Select(g => g.Header).Should().Equal("Documents", "Global");
        sut.Groups.SelectMany(g => g.Items).Select(i => i.Id)
            .Should().BeEquivalentTo("settings.open", "docs.refresh");
    }

    [Fact]
    public void Uses_scope_name_when_category_is_missing()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Descriptor("settings.open", "Open Settings", ShortcutScope.Global, VirtualKeyCode.OemComma, KeyModifiers.Ctrl));
        registry.Register(Descriptor("docs.refresh", "Refresh Documents", new ShortcutScope("DocumentsPage"), VirtualKeyCode.F5, KeyModifiers.None));

        var sut = new CheatsheetViewModel(registry, "DocumentsPage");

        sut.Groups.Select(g => g.Header).Should().Equal("DocumentsPage", "Global");
    }

    [Fact]
    public void Marks_groups_that_contain_current_scope_shortcuts()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Descriptor("settings.open", "Open Settings", ShortcutScope.Global, VirtualKeyCode.OemComma, KeyModifiers.Ctrl, "Global"));
        registry.Register(Descriptor("docs.refresh", "Refresh Documents", new ShortcutScope("DocumentsPage"), VirtualKeyCode.F5, KeyModifiers.None, "Documents"));

        var sut = new CheatsheetViewModel(registry, "DocumentsPage");

        sut.Groups.Single(g => g.Header == "Documents").IsCurrentScope.Should().BeTrue();
        sut.Groups.Single(g => g.Header == "Global").IsCurrentScope.Should().BeFalse();
    }

    [Fact]
    public void Orders_groups_and_items_by_display_text()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Descriptor("docs.z", "Zoom Document", new ShortcutScope("DocumentsPage"), VirtualKeyCode.Z, KeyModifiers.Ctrl, "Documents"));
        registry.Register(Descriptor("docs.a", "Archive Document", new ShortcutScope("DocumentsPage"), VirtualKeyCode.A, KeyModifiers.Ctrl, "Documents"));
        registry.Register(Descriptor("global.find", "Find", ShortcutScope.Global, VirtualKeyCode.F, KeyModifiers.Ctrl, "Global"));

        var sut = new CheatsheetViewModel(registry, "DocumentsPage");

        sut.Groups.Select(g => g.Header).Should().Equal("Documents", "Global");
        sut.Groups.Single(g => g.Header == "Documents").Items.Select(i => i.Label)
            .Should().Equal("Archive Document", "Zoom Document");
    }

    private static ShortcutDescriptor Descriptor(
        string id,
        string label,
        ShortcutScope scope,
        VirtualKeyCode key,
        KeyModifiers modifiers,
        string? category = null)
    {
        return new ShortcutDescriptor(
            id,
            label,
            scope,
            new[] { new KeyChord(modifiers, key) },
            _ => Task.CompletedTask,
            category);
    }
}
