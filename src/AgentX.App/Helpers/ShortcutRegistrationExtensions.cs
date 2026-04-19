using System;
using System.Collections.Generic;
using AgentX.Core.Services.Shortcuts;

namespace AgentX.App.Helpers;

public static class ShortcutRegistrationExtensions
{
    public static IDisposable RegisterShortcuts(
        this IShortcutRegistry registry,
        params ShortcutDescriptor[] descriptors)
    {
        var tokens = new List<IDisposable>(descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            tokens.Add(registry.Register(descriptor));
        }

        return new CompositeDisposable(tokens);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _tokens;
        private bool _disposed;

        public CompositeDisposable(List<IDisposable> tokens)
        {
            _tokens = tokens;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var token in _tokens)
            {
                token.Dispose();
            }
        }
    }
}
