using System;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class ChordStateMachineTests
{
    [Fact]
    public void Press_non_prefix_key_returns_None_result()
    {
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => DateTime.UtcNow);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        var result = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.A));

        result.Kind.Should().Be(ChordResultKind.None);
    }

    [Fact]
    public void Press_prefix_then_within_window_returns_ChordCompleted()
    {
        var t = DateTime.UtcNow;
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => t);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        var r1 = sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        t = t.AddMilliseconds(500);
        var r2 = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r1.Kind.Should().Be(ChordResultKind.PrefixArmed);
        r2.Kind.Should().Be(ChordResultKind.ChordCompleted);
        r2.CompletedChord.Should().HaveCount(2);
        r2.CompletedChord![0].Should().Be(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        r2.CompletedChord![1].Should().Be(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));
    }

    [Fact]
    public void Prefix_expires_after_window()
    {
        var t = DateTime.UtcNow;
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => t);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        t = t.AddMilliseconds(1200);
        var r = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r.Kind.Should().Be(ChordResultKind.None);
    }

    [Fact]
    public void Escape_cancels_armed_prefix()
    {
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => DateTime.UtcNow);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        sut.Reset();
        var r = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r.Kind.Should().Be(ChordResultKind.None);
    }
}
