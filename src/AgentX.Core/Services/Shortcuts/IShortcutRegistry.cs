using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Central registry for keyboard shortcuts. Pages register scope-local descriptors
/// during navigation; the <c>ShortcutInputRouter</c> queries this registry on every
/// key-down to route chords to the correct handler.
/// </summary>
public interface IShortcutRegistry
{
    /// <summary>Registers a shortcut. Returns a token that unregisters on Dispose.</summary>
    IDisposable Register(ShortcutDescriptor descriptor);

    /// <summary>All descriptors (global + every scope).</summary>
    IReadOnlyList<ShortcutDescriptor> All();

    /// <summary>Descriptors for Global + the given scope name.</summary>
    IReadOnlyList<ShortcutDescriptor> ForScope(string scopeName);

    /// <summary>Finds a descriptor matching the first chord key — used by input router.</summary>
    /// <param name="activeScopeName">
    /// Current page scope. Scope-specific matches beat global matches when both apply.
    /// Pass <c>null</c> for pre-navigation / global-only lookup.
    /// </param>
    ShortcutDescriptor? FindByPrimaryKey(KeyChord key, string? activeScopeName);

    /// <summary>Event fired on any registration change — palette VM refreshes from this.</summary>
    event EventHandler? Changed;
}
