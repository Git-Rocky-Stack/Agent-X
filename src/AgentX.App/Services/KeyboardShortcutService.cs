using Serilog;
using System;
using System.Collections.Generic;
using Windows.System;

namespace AgentX.App.Services;

/// <summary>
/// Registers and handles global keyboard shortcuts for the application.
/// Shortcuts are processed at the MainWindow level via KeyDown events.
/// Each shortcut is uniquely identified by a combination of virtual key and modifier flags.
/// </summary>
public sealed class KeyboardShortcutService
{
    /// <summary>
    /// Composite key identifying a unique keyboard shortcut.
    /// </summary>
    private readonly struct ShortcutKey : IEquatable<ShortcutKey>
    {
        public VirtualKey Key { get; }
        public bool Ctrl { get; }
        public bool Shift { get; }
        public bool Alt { get; }

        public ShortcutKey(VirtualKey key, bool ctrl, bool shift, bool alt)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public bool Equals(ShortcutKey other) =>
            Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;

        public override bool Equals(object? obj) =>
            obj is ShortcutKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Key, Ctrl, Shift, Alt);

        public override string ToString()
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }
    }

    private readonly Dictionary<ShortcutKey, Action> _shortcuts = new();

    /// <summary>
    /// Registers a keyboard shortcut with the specified modifier keys and action handler.
    /// If a shortcut with the same key combination already exists, it is overwritten.
    /// </summary>
    /// <param name="key">The primary virtual key.</param>
    /// <param name="ctrl">Whether Ctrl must be held.</param>
    /// <param name="shift">Whether Shift must be held.</param>
    /// <param name="alt">Whether Alt must be held.</param>
    /// <param name="handler">The action to execute when the shortcut is triggered.</param>
    public void RegisterShortcut(VirtualKey key, bool ctrl, bool shift, bool alt, Action handler)
    {
        var shortcutKey = new ShortcutKey(key, ctrl, shift, alt);

        if (_shortcuts.ContainsKey(shortcutKey))
        {
            Log.Warning("Overwriting existing keyboard shortcut: {Shortcut}", shortcutKey);
        }

        _shortcuts[shortcutKey] = handler;
        Log.Debug("Registered keyboard shortcut: {Shortcut}", shortcutKey);
    }

    /// <summary>
    /// Unregisters a previously registered keyboard shortcut.
    /// </summary>
    /// <param name="key">The primary virtual key.</param>
    /// <param name="ctrl">Whether Ctrl must be held.</param>
    /// <param name="shift">Whether Shift must be held.</param>
    /// <param name="alt">Whether Alt must be held.</param>
    public void UnregisterShortcut(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        var shortcutKey = new ShortcutKey(key, ctrl, shift, alt);

        if (_shortcuts.Remove(shortcutKey))
        {
            Log.Debug("Unregistered keyboard shortcut: {Shortcut}", shortcutKey);
        }
        else
        {
            Log.Warning("Attempted to unregister non-existent shortcut: {Shortcut}", shortcutKey);
        }
    }

    /// <summary>
    /// Attempts to match the given key combination against registered shortcuts.
    /// If a match is found, the associated handler is invoked.
    /// </summary>
    /// <param name="key">The primary virtual key that was pressed.</param>
    /// <param name="ctrl">Whether Ctrl is held.</param>
    /// <param name="shift">Whether Shift is held.</param>
    /// <param name="alt">Whether Alt is held.</param>
    /// <returns>True if a shortcut was matched and handled; false otherwise.</returns>
    public bool HandleKeyDown(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        var shortcutKey = new ShortcutKey(key, ctrl, shift, alt);

        if (_shortcuts.TryGetValue(shortcutKey, out var handler))
        {
            Log.Debug("Keyboard shortcut triggered: {Shortcut}", shortcutKey);

            try
            {
                handler.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing keyboard shortcut handler for {Shortcut}", shortcutKey);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the total number of registered shortcuts (primarily for diagnostics).
    /// </summary>
    public int RegisteredCount => _shortcuts.Count;
}
