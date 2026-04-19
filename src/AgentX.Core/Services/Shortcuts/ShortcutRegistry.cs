using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IShortcutRegistry"/>.
/// Uses a <see cref="ReaderWriterLockSlim"/> so multiple readers (the input router,
/// palette VM, cheatsheet VM) can query concurrently while writes (page navigation
/// register/unregister) are serialized.
/// </summary>
public sealed class ShortcutRegistry : IShortcutRegistry
{
    private readonly List<ShortcutDescriptor> _items = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public event EventHandler? Changed;

    public IDisposable Register(ShortcutDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

        _lock.EnterWriteLock();
        try { _items.Add(descriptor); }
        finally { _lock.ExitWriteLock(); }

        Changed?.Invoke(this, EventArgs.Empty);
        return new UnregisterToken(this, descriptor);
    }

    public IReadOnlyList<ShortcutDescriptor> All()
    {
        _lock.EnterReadLock();
        try { return _items.ToArray(); }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<ShortcutDescriptor> ForScope(string scopeName)
    {
        _lock.EnterReadLock();
        try
        {
            return _items
                .Where(d => d.Scope.IsGlobal || d.Scope.Name == scopeName)
                .ToArray();
        }
        finally { _lock.ExitReadLock(); }
    }

    public ShortcutDescriptor? FindByPrimaryKey(KeyChord key, string? activeScopeName)
    {
        _lock.EnterReadLock();
        try
        {
            // Scope-specific match beats global match when both match the same chord.
            var scoped = activeScopeName is null
                ? null
                : _items.FirstOrDefault(d => d.Scope.Name == activeScopeName && d.PrimaryKey == key);
            if (scoped is not null) return scoped;

            return _items.FirstOrDefault(d => d.Scope.IsGlobal && d.PrimaryKey == key);
        }
        finally { _lock.ExitReadLock(); }
    }

    private void Remove(ShortcutDescriptor descriptor)
    {
        bool removed;
        _lock.EnterWriteLock();
        try { removed = _items.Remove(descriptor); }
        finally { _lock.ExitWriteLock(); }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class UnregisterToken : IDisposable
    {
        private readonly ShortcutRegistry _registry;
        private readonly ShortcutDescriptor _descriptor;
        private bool _disposed;

        public UnregisterToken(ShortcutRegistry registry, ShortcutDescriptor descriptor)
        {
            _registry = registry;
            _descriptor = descriptor;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _registry.Remove(_descriptor);
        }
    }
}
