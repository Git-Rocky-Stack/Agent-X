using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CoreShortcutDescriptor = AgentX.Core.Services.Shortcuts.ShortcutDescriptor;

namespace AgentX.App.Services;

/// <summary>
/// Dispatches normalized shortcut chords to built-in keyboard surfaces or the
/// registry descriptor that matches the active page scope.
/// </summary>
public sealed partial class ShortcutInputRouter
{
    private readonly IShortcutRegistry _registry;
    private readonly ChordStateMachine _chords;
    private readonly Func<string?> _activeScopeProvider;
    private readonly Func<Task> _showPaletteAsync;
    private readonly Func<Task> _showJumpToAsync;
    private readonly Func<Task> _showCheatsheetAsync;

    public ShortcutInputRouter(
        IShortcutRegistry registry,
        ChordStateMachine chords,
        Func<string?> activeScopeProvider,
        Func<Task> showPaletteAsync,
        Func<Task> showJumpToAsync,
        Func<Task> showCheatsheetAsync)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _chords = chords ?? throw new ArgumentNullException(nameof(chords));
        _activeScopeProvider = activeScopeProvider ?? throw new ArgumentNullException(nameof(activeScopeProvider));
        _showPaletteAsync = showPaletteAsync ?? throw new ArgumentNullException(nameof(showPaletteAsync));
        _showJumpToAsync = showJumpToAsync ?? throw new ArgumentNullException(nameof(showJumpToAsync));
        _showCheatsheetAsync = showCheatsheetAsync ?? throw new ArgumentNullException(nameof(showCheatsheetAsync));
    }

    public async Task<bool> HandleAsync(KeyChord chord, CancellationToken ct = default)
    {
        var chordResult = _chords.OnKey(chord);
        if (chordResult.Kind == ChordResultKind.PrefixArmed)
        {
            return true;
        }

        if (chordResult.Kind == ChordResultKind.ChordCompleted)
        {
            var descriptor = FindCompletedChord(chordResult.CompletedChord);
            if (descriptor is not null)
            {
                await descriptor.Handler(ct);
            }

            return true;
        }

        if (chord == new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K)
            || chord == new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P))
        {
            await _showPaletteAsync();
            return true;
        }

        if (chord == new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P))
        {
            await _showJumpToAsync();
            return true;
        }

        if (chord == new KeyChord(KeyModifiers.None, VirtualKeyCode.F1)
            || chord == new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.Oem2))
        {
            await _showCheatsheetAsync();
            return true;
        }

        var activeScopeName = _activeScopeProvider();
        var matched = _registry.FindByPrimaryKey(chord, activeScopeName);
        if (matched is null)
        {
            return false;
        }

        await matched.Handler(ct);
        return true;
    }

    private CoreShortcutDescriptor? FindCompletedChord(IReadOnlyList<KeyChord>? completedChord)
    {
        if (completedChord is null || completedChord.Count == 0)
        {
            return null;
        }

        return _registry.All().FirstOrDefault(d => d.Chord.SequenceEqual(completedChord));
    }
}
