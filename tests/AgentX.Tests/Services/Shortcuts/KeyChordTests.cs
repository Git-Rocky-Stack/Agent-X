using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class KeyChordTests
{
    [Theory]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, "Ctrl+Shift+P")]
    [InlineData(KeyModifiers.Ctrl, VirtualKeyCode.P, "Ctrl+P")]
    [InlineData(KeyModifiers.None, VirtualKeyCode.Oem2, "?")]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeyCode.Delete, "Ctrl+Alt+Delete")]
    [InlineData(KeyModifiers.Ctrl, VirtualKeyCode.K, "Ctrl+K")]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.D1, "Ctrl+Shift+1")]
    [InlineData(KeyModifiers.None, VirtualKeyCode.F1, "F1")]
    public void Display_formats_modifiers_in_order_with_plus_separator(
        KeyModifiers mods, VirtualKeyCode key, string expected)
    {
        new KeyChord(mods, key).Display.Should().Be(expected);
    }

    [Fact]
    public void Value_equality_works_with_same_modifiers_and_key()
    {
        var a = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        var b = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        a.Should().Be(b);
    }

    [Fact]
    public void Value_inequality_on_modifier_difference()
    {
        var a = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        var b = new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P);
        a.Should().NotBe(b);
    }

    [Fact]
    public void DisplayChord_joins_multiple_chords_with_comma_space()
    {
        var descriptor = new ShortcutDescriptor(
            Id: "test.chord",
            Label: "Test Chord",
            Scope: ShortcutScope.Global,
            Chord: new[]
            {
                new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K),
                new KeyChord(KeyModifiers.None, VirtualKeyCode.D),
            },
            Handler: _ => System.Threading.Tasks.Task.CompletedTask);

        descriptor.DisplayChord.Should().Be("Ctrl+K, D");
        descriptor.IsChord.Should().BeTrue();
        descriptor.PrimaryKey.Should().Be(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
    }

    [Fact]
    public void Global_scope_is_well_known_singleton()
    {
        ShortcutScope.Global.IsGlobal.Should().BeTrue();
        ShortcutScope.Global.Name.Should().Be("Global");
    }

    [Fact]
    public void Scope_with_page_name_is_not_global()
    {
        var scope = new ShortcutScope("DocumentsPage");
        scope.IsGlobal.Should().BeFalse();
    }
}
