using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

public enum ChordResultKind
{
    None,            // key not part of any chord/prefix — handle normally (or ignore)
    PrefixArmed,     // first chord of multi-step just pressed — swallow key, wait for second
    ChordCompleted,  // second-step key pressed within window — fire the chord
}

public sealed record ChordResult(
    ChordResultKind Kind,
    IReadOnlyList<KeyChord>? CompletedChord = null);

/// <summary>
/// Tracks multi-step chord state ("Ctrl+K, D"). Registered prefixes are known upfront;
/// the first keypress arms a prefix and starts a timer window; a subsequent key within
/// the window completes the chord. Per Conflict 3 decision, v2.1.0 ships zero multi-step
/// chords in the seed catalog — this class is the infrastructure for future chords.
/// </summary>
public sealed class ChordStateMachine
{
    private readonly HashSet<KeyChord> _prefixes = new();
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _clock;

    private KeyChord? _armedPrefix;
    private DateTime _armedAt;

    public ChordStateMachine(int windowMs, Func<DateTime> clock)
    {
        _window = TimeSpan.FromMilliseconds(windowMs);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void RegisterPrefix(KeyChord prefix) => _prefixes.Add(prefix);

    public void UnregisterPrefix(KeyChord prefix) => _prefixes.Remove(prefix);

    public void Reset() => _armedPrefix = null;

    public ChordResult OnKey(KeyChord key)
    {
        // If a prefix is armed and we're inside the window, this key completes a chord.
        if (_armedPrefix is not null)
        {
            if (_clock() - _armedAt <= _window)
            {
                var completed = new[] { _armedPrefix, key };
                _armedPrefix = null;
                return new ChordResult(ChordResultKind.ChordCompleted, completed);
            }
            // Window expired — discard armed state and fall through to normal handling.
            _armedPrefix = null;
        }

        // Is this key a registered prefix? Arm it.
        if (_prefixes.Contains(key))
        {
            _armedPrefix = key;
            _armedAt = _clock();
            return new ChordResult(ChordResultKind.PrefixArmed);
        }

        return new ChordResult(ChordResultKind.None);
    }
}
