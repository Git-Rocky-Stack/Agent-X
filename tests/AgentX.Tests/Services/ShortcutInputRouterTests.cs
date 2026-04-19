using System.Threading.Tasks;
using AgentX.App.Services;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

public class ShortcutInputRouterTests
{
    [Fact]
    public async Task HandleAsync_invokes_palette_for_modern_palette_trigger()
    {
        var paletteCalls = 0;
        var sut = CreateRouter(showPaletteAsync: () =>
        {
            paletteCalls++;
            return Task.CompletedTask;
        });

        var handled = await sut.HandleAsync(new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P));

        handled.Should().BeTrue();
        paletteCalls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_invokes_jump_to_for_ctrl_p()
    {
        var jumpCalls = 0;
        var sut = CreateRouter(showJumpToAsync: () =>
        {
            jumpCalls++;
            return Task.CompletedTask;
        });

        var handled = await sut.HandleAsync(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P));

        handled.Should().BeTrue();
        jumpCalls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_invokes_active_scope_registry_descriptor()
    {
        var invoked = false;
        var registry = new ShortcutRegistry();
        registry.Register(new ShortcutDescriptor(
            "docs.refresh",
            "Refresh Documents",
            new ShortcutScope("DocumentsPage"),
            new[] { new KeyChord(KeyModifiers.None, VirtualKeyCode.F5) },
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            }));
        var sut = CreateRouter(registry, activeScopeName: "DocumentsPage");

        var handled = await sut.HandleAsync(new KeyChord(KeyModifiers.None, VirtualKeyCode.F5));

        handled.Should().BeTrue();
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_returns_false_for_unmatched_chord()
    {
        var sut = CreateRouter();

        var handled = await sut.HandleAsync(new KeyChord(KeyModifiers.Alt, VirtualKeyCode.Z));

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_swallows_registered_prefix_without_invoking_dialogs()
    {
        var paletteCalls = 0;
        var chords = new ChordStateMachine(1000, () => System.DateTime.UtcNow);
        chords.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.Oem2));
        var sut = CreateRouter(
            chords: chords,
            showPaletteAsync: () =>
            {
                paletteCalls++;
                return Task.CompletedTask;
            });

        var handled = await sut.HandleAsync(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.Oem2));

        handled.Should().BeTrue();
        paletteCalls.Should().Be(0);
    }

    private static ShortcutInputRouter CreateRouter(
        IShortcutRegistry? registry = null,
        ChordStateMachine? chords = null,
        string? activeScopeName = null,
        Func<Task>? showPaletteAsync = null,
        Func<Task>? showJumpToAsync = null,
        Func<Task>? showCheatsheetAsync = null)
    {
        return new ShortcutInputRouter(
            registry ?? new ShortcutRegistry(),
            chords ?? new ChordStateMachine(1000, () => System.DateTime.UtcNow),
            () => activeScopeName,
            showPaletteAsync ?? (() => Task.CompletedTask),
            showJumpToAsync ?? (() => Task.CompletedTask),
            showCheatsheetAsync ?? (() => Task.CompletedTask));
    }
}
