using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Bit-flag modifier set. Stays in <c>AgentX.Core</c> (no <c>Windows.System</c> dependency)
/// so the shortcut model is testable without the Windows App SDK.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

/// <summary>
/// Platform-neutral key codes for the chord vocabulary Agent-X supports.
/// <see cref="AgentX.App.Services.ShortcutInputRouter"/> maps
/// <c>Windows.System.VirtualKey</c> → <c>VirtualKeyCode</c> at the boundary.
/// </summary>
public enum VirtualKeyCode
{
    None = 0,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Enter, Escape, Tab, Space, Backspace, Delete,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    Oem2,    // "?" / "/"
    OemPlus, // "+"
    OemMinus,// "-"
    OemComma,// ","
    OemPeriod,// "."
}

/// <summary>
/// Immutable representation of a keyboard combo (modifiers + a single key).
/// Supports multi-step chords when multiple <see cref="KeyChord"/> values are chained
/// via <see cref="ShortcutDescriptor.Chord"/>.
/// </summary>
public sealed record KeyChord(
    KeyModifiers Modifiers,
    VirtualKeyCode Key)
{
    /// <summary>Human-readable display — e.g., <c>"Ctrl+Shift+P"</c>.</summary>
    public string Display => KeyChordFormatter.Format(this);
}

public static class KeyChordFormatter
{
    public static string Format(KeyChord c)
    {
        var parts = new List<string>(5);
        if (c.Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (c.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (c.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (c.Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(FormatKey(c.Key));
        return string.Join("+", parts);
    }

    private static string FormatKey(VirtualKeyCode k) => k switch
    {
        VirtualKeyCode.Oem2 => "?",
        VirtualKeyCode.OemPlus => "+",
        VirtualKeyCode.OemMinus => "-",
        VirtualKeyCode.OemComma => ",",
        VirtualKeyCode.OemPeriod => ".",
        VirtualKeyCode.D0 => "0",
        VirtualKeyCode.D1 => "1",
        VirtualKeyCode.D2 => "2",
        VirtualKeyCode.D3 => "3",
        VirtualKeyCode.D4 => "4",
        VirtualKeyCode.D5 => "5",
        VirtualKeyCode.D6 => "6",
        VirtualKeyCode.D7 => "7",
        VirtualKeyCode.D8 => "8",
        VirtualKeyCode.D9 => "9",
        _ => k.ToString(),
    };
}
