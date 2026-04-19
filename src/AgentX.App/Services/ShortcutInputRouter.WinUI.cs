using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace AgentX.App.Services;

public sealed partial class ShortcutInputRouter
{
    public void Attach(FrameworkElement root)
    {
        root.PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var chord = ToKeyChord(e.Key);
        if (chord is null)
        {
            return;
        }

        if (await HandleAsync(chord))
        {
            e.Handled = true;
        }
    }

    internal static KeyChord? ToKeyChord(VirtualKey key)
    {
        var mapped = MapVirtualKey(key);
        if (mapped == VirtualKeyCode.None)
        {
            return null;
        }

        var modifiers = KeyModifiers.None;
        if ((InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            modifiers |= KeyModifiers.Ctrl;
        }

        if ((InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if ((InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            modifiers |= KeyModifiers.Alt;
        }

        return new KeyChord(modifiers, mapped);
    }

    internal static VirtualKeyCode MapVirtualKey(VirtualKey key) => key switch
    {
        VirtualKey.A => VirtualKeyCode.A,
        VirtualKey.B => VirtualKeyCode.B,
        VirtualKey.C => VirtualKeyCode.C,
        VirtualKey.D => VirtualKeyCode.D,
        VirtualKey.E => VirtualKeyCode.E,
        VirtualKey.F => VirtualKeyCode.F,
        VirtualKey.G => VirtualKeyCode.G,
        VirtualKey.H => VirtualKeyCode.H,
        VirtualKey.I => VirtualKeyCode.I,
        VirtualKey.J => VirtualKeyCode.J,
        VirtualKey.K => VirtualKeyCode.K,
        VirtualKey.L => VirtualKeyCode.L,
        VirtualKey.M => VirtualKeyCode.M,
        VirtualKey.N => VirtualKeyCode.N,
        VirtualKey.O => VirtualKeyCode.O,
        VirtualKey.P => VirtualKeyCode.P,
        VirtualKey.Q => VirtualKeyCode.Q,
        VirtualKey.R => VirtualKeyCode.R,
        VirtualKey.S => VirtualKeyCode.S,
        VirtualKey.T => VirtualKeyCode.T,
        VirtualKey.U => VirtualKeyCode.U,
        VirtualKey.V => VirtualKeyCode.V,
        VirtualKey.W => VirtualKeyCode.W,
        VirtualKey.X => VirtualKeyCode.X,
        VirtualKey.Y => VirtualKeyCode.Y,
        VirtualKey.Z => VirtualKeyCode.Z,
        VirtualKey.Number0 => VirtualKeyCode.D0,
        VirtualKey.Number1 => VirtualKeyCode.D1,
        VirtualKey.Number2 => VirtualKeyCode.D2,
        VirtualKey.Number3 => VirtualKeyCode.D3,
        VirtualKey.Number4 => VirtualKeyCode.D4,
        VirtualKey.Number5 => VirtualKeyCode.D5,
        VirtualKey.Number6 => VirtualKeyCode.D6,
        VirtualKey.Number7 => VirtualKeyCode.D7,
        VirtualKey.Number8 => VirtualKeyCode.D8,
        VirtualKey.Number9 => VirtualKeyCode.D9,
        VirtualKey.F1 => VirtualKeyCode.F1,
        VirtualKey.F2 => VirtualKeyCode.F2,
        VirtualKey.F3 => VirtualKeyCode.F3,
        VirtualKey.F4 => VirtualKeyCode.F4,
        VirtualKey.F5 => VirtualKeyCode.F5,
        VirtualKey.F6 => VirtualKeyCode.F6,
        VirtualKey.F7 => VirtualKeyCode.F7,
        VirtualKey.F8 => VirtualKeyCode.F8,
        VirtualKey.F9 => VirtualKeyCode.F9,
        VirtualKey.F10 => VirtualKeyCode.F10,
        VirtualKey.F11 => VirtualKeyCode.F11,
        VirtualKey.F12 => VirtualKeyCode.F12,
        VirtualKey.Enter => VirtualKeyCode.Enter,
        VirtualKey.Escape => VirtualKeyCode.Escape,
        VirtualKey.Tab => VirtualKeyCode.Tab,
        VirtualKey.Space => VirtualKeyCode.Space,
        VirtualKey.Back => VirtualKeyCode.Backspace,
        VirtualKey.Delete => VirtualKeyCode.Delete,
        VirtualKey.Left => VirtualKeyCode.Left,
        VirtualKey.Right => VirtualKeyCode.Right,
        VirtualKey.Up => VirtualKeyCode.Up,
        VirtualKey.Down => VirtualKeyCode.Down,
        VirtualKey.Home => VirtualKeyCode.Home,
        VirtualKey.End => VirtualKeyCode.End,
        VirtualKey.PageUp => VirtualKeyCode.PageUp,
        VirtualKey.PageDown => VirtualKeyCode.PageDown,
        (VirtualKey)191 => VirtualKeyCode.Oem2,
        (VirtualKey)187 => VirtualKeyCode.OemPlus,
        (VirtualKey)189 => VirtualKeyCode.OemMinus,
        (VirtualKey)188 => VirtualKeyCode.OemComma,
        (VirtualKey)190 => VirtualKeyCode.OemPeriod,
        _ => VirtualKeyCode.None,
    };
}
