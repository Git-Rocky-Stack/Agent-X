using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// A single registered shortcut. Immutable. Handler is an async delegate so palette
/// commands can navigate, show dialogs, or invoke services without blocking the
/// key-input pipeline.
/// </summary>
public sealed record ShortcutDescriptor(
    string Id,                              // stable, e.g. "doc.import" — telemetry + future config
    string Label,                           // localized UI label, e.g. "Import Document…"
    ShortcutScope Scope,                    // Global or a page name
    IReadOnlyList<KeyChord> Chord,          // 1 element for simple, N for multi-step
    Func<CancellationToken, Task> Handler,
    string? Category = null)                // optional grouping label for cheatsheet
{
    public KeyChord PrimaryKey => Chord[0];
    public bool IsChord => Chord.Count > 1;
    public string DisplayChord => string.Join(", ", Chord.Select(k => k.Display));
}
