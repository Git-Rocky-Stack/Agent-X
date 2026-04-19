using System.Linq;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class ShortcutRegistryTests
{
    [Fact]
    public void Register_adds_descriptor_and_fires_changed()
    {
        var sut = new ShortcutRegistry();
        int changedFiredCount = 0;
        sut.Changed += (_, _) => changedFiredCount++;

        using var _ = sut.Register(NewDescriptor("a", KeyModifiers.Ctrl, VirtualKeyCode.A));

        sut.All().Should().HaveCount(1);
        changedFiredCount.Should().Be(1);
    }

    [Fact]
    public void Register_disposal_unregisters_and_fires_changed()
    {
        var sut = new ShortcutRegistry();
        var token = sut.Register(NewDescriptor("a", KeyModifiers.Ctrl, VirtualKeyCode.A));
        int changedAfterFirst = 0;
        sut.Changed += (_, _) => changedAfterFirst++;

        token.Dispose();

        sut.All().Should().BeEmpty();
        changedAfterFirst.Should().Be(1);
    }

    [Fact]
    public void ForScope_returns_global_plus_scope_descriptors_only()
    {
        var sut = new ShortcutRegistry();
        sut.Register(NewDescriptor("g", KeyModifiers.Ctrl, VirtualKeyCode.G, ShortcutScope.Global));
        sut.Register(NewDescriptor("docs.import", KeyModifiers.Ctrl, VirtualKeyCode.I, new ShortcutScope("DocumentsPage")));
        sut.Register(NewDescriptor("chat.clear", KeyModifiers.Ctrl, VirtualKeyCode.L, new ShortcutScope("ChatPage")));

        var forDocs = sut.ForScope("DocumentsPage");

        forDocs.Select(d => d.Id).Should().BeEquivalentTo("g", "docs.import");
    }

    [Fact]
    public void FindByPrimaryKey_matches_global_regardless_of_active_scope()
    {
        var sut = new ShortcutRegistry();
        var global = NewDescriptor("palette", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, ShortcutScope.Global);
        sut.Register(global);

        var match = sut.FindByPrimaryKey(
            new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P),
            activeScopeName: "AnyPage");

        match.Should().NotBeNull();
        match!.Id.Should().Be("palette");
    }

    [Fact]
    public void FindByPrimaryKey_scope_beats_global_when_both_match()
    {
        var sut = new ShortcutRegistry();
        sut.Register(NewDescriptor("global.refresh", KeyModifiers.None, VirtualKeyCode.F5, ShortcutScope.Global));
        sut.Register(NewDescriptor("docs.refresh", KeyModifiers.None, VirtualKeyCode.F5, new ShortcutScope("DocumentsPage")));

        var match = sut.FindByPrimaryKey(
            new KeyChord(KeyModifiers.None, VirtualKeyCode.F5),
            activeScopeName: "DocumentsPage");

        match!.Id.Should().Be("docs.refresh");
    }

    [Fact]
    public void FindByPrimaryKey_returns_null_when_no_match()
    {
        var sut = new ShortcutRegistry();

        var match = sut.FindByPrimaryKey(
            new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.X),
            activeScopeName: null);

        match.Should().BeNull();
    }

    private static ShortcutDescriptor NewDescriptor(
        string id,
        KeyModifiers mods,
        VirtualKeyCode key,
        ShortcutScope? scope = null)
        => new(
            Id: id,
            Label: $"Label-{id}",
            Scope: scope ?? ShortcutScope.Global,
            Chord: new[] { new KeyChord(mods, key) },
            Handler: _ => Task.CompletedTask,
            Category: null);
}
