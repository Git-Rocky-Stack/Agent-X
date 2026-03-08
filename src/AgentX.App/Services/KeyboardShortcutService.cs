using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;

namespace AgentX.App.Services;

/// <summary>
/// Describes a registered keyboard shortcut for display in the shortcut overlay.
/// </summary>
public sealed class ShortcutDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string KeyCombo { get; init; }
    public VirtualKey Key { get; init; }
    public bool Ctrl { get; init; }
    public bool Shift { get; init; }
    public bool Alt { get; init; }
}

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
            parts.Add(FormatKeyName(Key));
            return string.Join("+", parts);
        }

        private static string FormatKeyName(VirtualKey key) => key switch
        {
            (VirtualKey)188 => ",",
            (VirtualKey)190 => ".",
            (VirtualKey)191 => "/",
            (VirtualKey)186 => ";",
            (VirtualKey)222 => "'",
            (VirtualKey)219 => "[",
            (VirtualKey)221 => "]",
            (VirtualKey)220 => "\\",
            (VirtualKey)189 => "-",
            (VirtualKey)187 => "=",
            _ => key.ToString()
        };
    }

    private readonly Dictionary<ShortcutKey, Action> _shortcuts = new();
    private readonly Dictionary<ShortcutKey, ShortcutDescriptor> _descriptors = new();

    /// <summary>
    /// Registers a keyboard shortcut with the specified modifier keys, action handler,
    /// and optional metadata for the shortcuts overlay.
    /// </summary>
    public void RegisterShortcut(VirtualKey key, bool ctrl, bool shift, bool alt, Action handler,
        string? id = null, string? displayName = null, string? category = null)
    {
        var shortcutKey = new ShortcutKey(key, ctrl, shift, alt);

        if (_shortcuts.ContainsKey(shortcutKey))
        {
            Log.Warning("Overwriting existing keyboard shortcut: {Shortcut}", shortcutKey);
        }

        _shortcuts[shortcutKey] = handler;

        if (id is not null && displayName is not null)
        {
            _descriptors[shortcutKey] = new ShortcutDescriptor
            {
                Id = id,
                DisplayName = displayName,
                Category = category ?? "General",
                KeyCombo = shortcutKey.ToString(),
                Key = key,
                Ctrl = ctrl,
                Shift = shift,
                Alt = alt
            };
        }

        Log.Debug("Registered keyboard shortcut: {Shortcut}", shortcutKey);
    }

    /// <summary>
    /// Unregisters a previously registered keyboard shortcut.
    /// </summary>
    public void UnregisterShortcut(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        var shortcutKey = new ShortcutKey(key, ctrl, shift, alt);

        if (_shortcuts.Remove(shortcutKey))
        {
            _descriptors.Remove(shortcutKey);
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
    /// Gets all registered shortcuts with their descriptive metadata, grouped by category.
    /// Used by the keyboard shortcut help overlay.
    /// </summary>
    public IReadOnlyList<ShortcutDescriptor> GetAllShortcuts() =>
        _descriptors.Values
            .OrderBy(s => s.Category)
            .ThenBy(s => s.DisplayName)
            .ToList();

    /// <summary>
    /// Gets shortcuts filtered by category.
    /// </summary>
    public IReadOnlyList<ShortcutDescriptor> GetShortcutsByCategory(string category) =>
        _descriptors.Values
            .Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.DisplayName)
            .ToList();

    /// <summary>
    /// Gets all distinct categories that have registered shortcuts.
    /// </summary>
    public IReadOnlyList<string> GetCategories() =>
        _descriptors.Values
            .Select(s => s.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    /// <summary>
    /// Gets the total number of registered shortcuts (primarily for diagnostics).
    /// </summary>
    public int RegisteredCount => _shortcuts.Count;
}
