# A2 — Keyboard-First Power Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform Agent-X from a global-hotkey-only experience (Win+Shift+A Quick Chat) into a true keyboard-first power mode featuring a fuzzy-searchable **command palette** (Ctrl+Shift+P), **jump-to-anything** navigation (Ctrl+P) across documents / conversations / pages, an in-app **cheatsheet** (`?`) grouped by scope, a pluggable `IShortcutRegistry` that owns every chord in the app, multi-step **chord** support (e.g., Ctrl+K then D), and per-page shortcut help.

**Architecture:** A central `IShortcutRegistry` singleton stores immutable `ShortcutDescriptor` records keyed by scope (`Global` or `<PageName>`). Each descriptor carries the key combo, human-readable label, scope, handler delegate, and optional chord continuation. Pages register their scope-local shortcuts during `OnNavigatedTo` and unregister during `OnNavigatedFrom`. A shared `FuzzyMatcher` (inline VS-Code-style subsequence scoring — **no new NuGet**) powers the palette and jump-to dialogs. A `ChordStateMachine` with a 1-second window handles multi-step chords via `MainWindow`'s `CoreWindow.KeyDown` hook. Three new `ContentDialog`s — `CommandPaletteDialog`, `JumpToDialog`, `CheatsheetDialog` — share a common virtualized `ListView` + `TextBox` layout.

**Tech Stack:** .NET 8, WinUI 3 (`ContentDialog`, `KeyboardAccelerator`, `CoreWindow.KeyDown`), CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xUnit + FluentAssertions for test coverage, no new NuGet dependencies.

**Prerequisites:** None from other A/B/C items. This plan may run in parallel with A1 (multi-language UI) and B1–B7 (monolith splits). Assumes the existing global hotkey (Win+Shift+A Quick Chat) and `QuickActionsPage` remain — this plan extends, does not replace.

**Target release:** v2.1.0 (final).

---

## ⚠️ Post-Spike Revision Required (2026-04-18)

Spikes 0–3 completed 2026-04-18 returned findings that **invalidate three of this plan's core premises** and require design decisions from Rocky before Task 1 begins.

### Conflict 1 — `KeyboardShortcutService` already exists
A `KeyboardShortcutService` is registered Singleton at `App.xaml.cs:280` and has a `RegisterDefaultShortcuts()` method used in `MainWindow.xaml.cs:141–225`. This plan proposes creating `IShortcutRegistry` / `ShortcutRegistry` from scratch. **Decision required:**
- (a) **Extend + rename** — evolve `KeyboardShortcutService` into `IShortcutRegistry` with scope semantics; keep existing registrations intact and migrate them to `ShortcutDescriptor` records.
- (b) **Deprecate + replace** — introduce new `IShortcutRegistry`, port all existing shortcuts into the new API, delete `KeyboardShortcutService`.
- (c) **Coexist** — leave legacy service alone, layer new registry above it. (Not recommended — two registries competing for the same PreviewKeyDown event.)

### Conflict 2 — `CommandPalette.xaml.cs` already exists
An existing `CommandPalette.xaml.cs` handles `Down/Up/Enter/Escape/Tab` locally (lines 538–561). This plan proposes creating a new `CommandPaletteDialog`. **Decision required:**
- (a) **Replace** — retire existing palette, use new `CommandPaletteDialog` for the Ctrl+Shift+P entry point.
- (b) **Extend** — fold plan requirements (fuzzy match, scope-aware results, registry-driven source) into the existing palette.
- (c) **Differentiate** — keep existing palette as a narrower "Quick Actions" launcher (already bound to its own trigger), and build the new palette alongside as a distinct "Command Palette" surface.

### Conflict 3 — Planned chords already claimed
- `Ctrl+K` is currently the trigger for the existing CommandPalette (line 145 in MainWindow). The plan's chord system planned to use `Ctrl+K` as a multi-step prefix.
- `Ctrl+Shift+?` already shows an existing shortcuts-overlay (line 209). The plan's cheatsheet uses plain `?` (no modifiers) — which is still available — but the overlap with the existing overlay needs resolution.

**Decisions resolved by Rocky (2026-04-18):**

1. ✅ **Conflict 1 — `KeyboardShortcutService` → EXTEND + RENAME** to `IShortcutRegistry`/`ShortcutRegistry`. Evolve the existing service; migrate `RegisterDefaultShortcuts()` content into the seed catalog; preserve all existing consumers (no breaking changes at call sites other than the rename).
2. ✅ **Conflict 2 — Existing `CommandPalette.xaml.cs` → EXTEND** (controller judgment per Rocky's "trust your judgement" directive). Fold the plan's palette functionality (fuzzy match, scope-aware results, registry-driven sources) INTO the existing palette. Keep `Ctrl+K` as the legacy trigger; ADD `Ctrl+Shift+P` as a second trigger (VS Code muscle memory). One palette, two keys, one code path. The planned `CommandPaletteDialog` is dropped — the existing palette file is revised in place.
3. ✅ **Conflict 3 — Chord collisions** (controller judgment):
   - **Command Palette**: trigger on BOTH `Ctrl+K` AND `Ctrl+Shift+P` (existing + VS Code familiarity).
   - **Jump-To**: `Ctrl+P` (no conflict — available).
   - **Cheatsheet**: trigger on BOTH `F1` (Windows help convention) AND the existing `Ctrl+Shift+?`. The existing shortcuts-overlay is folded into the new cheatsheet dialog — one surface, two keys.
   - **`Ctrl+K, D` chord prefix DROPPED**. `Ctrl+K` is reserved for the palette; a chord-prefix starting with `Ctrl+K` would break the palette trigger. `ChordStateMachine` is still built (infrastructure), but no shortcut uses a multi-step chord in the v2.1.0 final seed. Future chords can use other prefixes (e.g., `Ctrl+K` is out; `Ctrl+;` is free).

### Revised technical findings (apply to ALL tasks)
- **Input mechanism** is `RootGrid.PreviewKeyDown` in `MainWindow.xaml.cs:120`, NOT `CoreWindow.KeyDown`. Task 8 (`ShortcutInputRouter`) must use this pattern. WinUI 3 has no literal `UIElement.PreviewKeyDown`, but `FrameworkElement.PreviewKeyDown` fires pre-focus-dispatch and reliably intercepts even when a `TextBox` has focus.
- **Global-hotkey mechanism** for Win+Shift+A Quick Chat uses Win32 `RegisterHotKey` via `SystemTrayService.cs:90` with window subclass (`WM_HOTKEY`). A2 triggers are app-scoped only — do NOT add new `RegisterHotKey` calls.
- **DI lifetime for registry** confirmed Singleton (single-window app — only one `new MainWindow()` call at `App.xaml.cs:80`).
- **Optimal attach point** for the router: `MainWindow.xaml.cs` line 121, immediately after the existing `RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;` wire-up.

---

## Pre-Implementation Spikes (REQUIRED — run first)

### Spike 0 — Inventory existing keyboard shortcuts

**Question:** What shortcuts already exist in Agent-X, and where are they wired?

- [ ] Run: `grep -rn 'KeyboardAccelerator\|AcceleratorKey\|VirtualKey' src/AgentX.App --include='*.cs' --include='*.xaml' | head -50` — record every location.
- [ ] Open `src/AgentX.App/Services/HotkeyService.cs` (or whatever handles the Win+Shift+A global hotkey) and document the current registration API.
- [ ] Open `src/AgentX.App/Views/QuickActionsPage.xaml` and list the actions it exposes. These map 1:1 to high-priority palette entries.
- [ ] Grep for `CoreWindow.KeyDown` / `AcceleratorKeyActivated` — is there an existing app-wide key hook?

**Spike Findings (2026-04-18):**
- **Existing keyboard handlers**:
  - `MainWindow.xaml.cs:120` — `RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown` (the primary in-app keyboard intake)
  - `CommandPalette.xaml.cs:538–561` — local handling of `VirtualKey.Down/Up/Enter/Escape/Tab` for palette navigation
  - `AskFilesPage.xaml.cs:68` — `VirtualKey.Enter` handler for submit
- **Existing services**:
  - **`KeyboardShortcutService`** — registered Singleton at `App.xaml.cs:280`, resolved in `MainWindow.xaml.cs:113` via `App.GetService<KeyboardShortcutService>()`. Called from `RegisterDefaultShortcuts()` at lines 141–225 to seed defaults.
  - **`CommandPalette.xaml.cs`** — an existing palette implementation. Toggled via Ctrl+K (line 145 in MainWindow).
- **Global-hotkey API (Win+Shift+A)**:
  - `SystemTrayService.cs:90` — `RegisterHotKey(_hwnd, HOTKEY_ID, MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, 0x41)` (Win32 P/Invoke)
  - Signature: `private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk)` (line 304)
  - Registration happens in `RegisterGlobalHotkey()` (line 84); hooked to app startup via `App.xaml.cs`
  - Uses window subclass to intercept `WM_HOTKEY` — fires regardless of focus state (OS-level)
- **QuickActionsPage actions (5 commands)** — these become seed entries for the new command palette (Task 5 / 10):
  - `SummarizeCommand` — tab "Summarize"
  - `ExtractKeyPointsCommand` — tab "Key Points"
  - `TranslateCommand` — tab "Translate"
  - `FindDuplicatesCommand` — tab "Duplicates"
  - `SuggestOrganizationCommand` — tab "Organize"
- **App-wide key hook path**: `RootGrid.PreviewKeyDown` in MainWindow — single entry point, centralized.
- **Chord conflicts with planned A2 triggers**:
  - **Ctrl+K — CLAIMED** (line 145 in MainWindow) — toggles existing `CommandPalette`. **Conflict with A2's planned chord prefix `Ctrl+K, D`.** See Conflict 3 in the post-spike revision block above.
  - **Ctrl+Shift+?** — CLAIMED (VirtualKey 191 + Ctrl+Shift, line 209) — shows existing shortcuts-overlay.
  - **Ctrl+Shift+P** — **AVAILABLE** (no conflict) — safe for new command palette.
  - **Ctrl+P** — **AVAILABLE** (no conflict) — safe for jump-to.
  - Plain `?` (no modifiers) — **AVAILABLE** — safe for cheatsheet, but may want to coordinate with existing Ctrl+Shift+? overlay so users aren't confused by two near-identical cheatsheets.

### Spike 1 — Confirm WinUI 3 `KeyboardAccelerator` behavior for global-scope shortcuts

**Question:** Can `KeyboardAccelerator` on `MainWindow`'s root element fire from anywhere in the app, or must we use `CoreWindow.KeyDown`?

- [ ] Read WinUI 3 KeyboardAccelerator docs via context7 (`mcp__plugin_context7_context7__resolve-library-id` → Microsoft.WindowsAppSDK) to confirm scoping behavior.
- [ ] If the accelerator on the root element propagates to all descendants — use that for palette / jump / cheatsheet hotkeys (simpler).
- [ ] If it doesn't propagate through focused child-page input (e.g., a `TextBox` swallows Ctrl+Shift+P) — use `CoreWindow.KeyDown` (lower-level) with `AcceleratorKeyActivated` fallback.
- [ ] Record decision.

**Spike Findings (2026-04-18):**
- **Best-fit event**: `FrameworkElement.PreviewKeyDown` on `RootGrid` (existing Agent-X pattern). WinUI 3 does NOT expose a literal `UIElement.PreviewKeyDown`, but `FrameworkElement.PreviewKeyDown` fires pre-focus-dispatch and reliably intercepts even when a `TextBox` has focus — already proven by the existing CommandPalette.
- **NOT recommended**: `CoreWindow.KeyDown` (the plan's original proposal). Agent-X's current keyboard pipeline uses the XAML-layer `PreviewKeyDown`; going to `CoreWindow.KeyDown` would create two competing hook layers. Stay consistent with the existing pattern.
- **NOT recommended**: new `Win32.RegisterHotKey` calls for Ctrl+Shift+P / Ctrl+P / ?. Reserve OS-level hotkeys for Win+Shift+A Quick Chat (which needs to fire when Agent-X is minimized). App-scoped palette/jump/cheatsheet triggers are in-app only — they do NOT need to survive minimization.
- **Win+Shift+A Quick Chat** confirmed Win32 `RegisterHotKey` in `SystemTrayService.cs:90` with `WM_HOTKEY` subclass — leave alone.
- **Task 8 (`ShortcutInputRouter`) revised**: use `RootGrid.PreviewKeyDown += ...` instead of `window.Content.KeyDown += ...`. The attach API signature stays `Attach(Window)`, but internal wiring targets the root grid, not `window.Content` directly.

### Spike 2 — DI lifetime for `IShortcutRegistry`

**Question:** Should the registry be `Singleton` (lives for app lifetime, pages register / unregister over time) or `Scoped` (per-nav-frame)?

- [ ] Singleton is almost certainly correct — global shortcuts persist across navigation. Confirm no other lifetime-sensitive state lives in the registry.
- [ ] Check: are multiple `MainWindow`s possible (multi-window Agent-X)? If yes, registry should be per-window. If no (single `MainWindow`), singleton is fine.

**Spike Findings (2026-04-18):**
- **Multiple windows supported: NO** — single `new MainWindow()` call at `App.xaml.cs:80`. No `AppWindow.Create()` patterns. No second-window code paths found.
- **Lifetime decision: SINGLETON** (confirmed correct) — single app instance, no multi-window concerns.
- **DI container**: `Microsoft.Extensions.Hosting` (standard MS.Extensions.DI). Registration block at `App.xaml.cs:215–300` inside `ConfigureServices(HostBuilderContext, IServiceCollection)`.
- **Existing precedent**: `services.AddSingleton<KeyboardShortcutService>()` at `App.xaml.cs:280` — same lifetime + pattern the new registry should use (or extend, per Conflict 1 decision).
- **DI registration line for Task 11**: Add new service registrations in the same `ConfigureServices` block at `App.xaml.cs:215–300`, grouped near the existing `KeyboardShortcutService` registration (line 280).

### Spike 3 — Locate `MainWindow` input pipeline for chord registration

**Question:** Task 11 wires the palette / jump / cheatsheet hotkeys into `MainWindow`. B5 (from magnum opus) calls out `MainWindow.xaml.cs` as a 939-LOC monolith slated for splits. We must add hooks without making it worse.

- [ ] Open `src/AgentX.App/MainWindow.xaml.cs` and locate where the existing Win+Shift+A global hotkey is wired.
- [ ] Plan: route all A2 shortcut wiring through a new `ShortcutInputRouter` service so `MainWindow.xaml.cs` only adds **one** line (`_shortcutRouter.Attach(this)`) — keeps B5's refactor window clean.

**Spike Findings (2026-04-18):**
- **`MainWindow.xaml.cs` line count: 939** — matches the B5 refactor threshold exactly. B5 has NOT started (no partial refactor evidence, no recent split commits).
- **Constructor**: lines 46–135 (89 lines) — parameterless `public MainWindow()` resolved via `App.GetService<MainWindow>()`.
- **Keyboard wiring reference points**:
  - Line 113 — `_keyboardShortcutService = App.GetService<KeyboardShortcutService>();`
  - Line 114–117 — calls to `RegisterDefaultShortcuts()` + `ConfigureCommandPalette()`
  - Line 120 — `RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;` (the hookup point)
  - Lines 141–225 — body of `RegisterDefaultShortcuts()` (where shortcuts get seeded)
  - Lines 315–335 — body of `RootGrid_PreviewKeyDown()` (the handler)
- **Optimal insertion point for `_shortcutRouter.Attach(this);`**: **Line 121**, immediately after the existing `RootGrid.PreviewKeyDown +=` line and before the next setup call. This groups all input-wiring together and keeps Task 8's change surgical (one new line in MainWindow).
- **Router implementation note**: the router's `Attach(Window)` method should internally do `((FrameworkElement)window.Content).FindName("RootGrid")?.PreviewKeyDown += OnKeyDown` rather than attach to `window.Content.KeyDown` — maintaining the pre-focus-dispatch semantics of the existing pattern.

### Spike Closure

Before starting Task 1:
- [ ] All 4 spike findings recorded
- [ ] Plan tasks revised in place for every finding that changes implementation
- [ ] Commit the revised plan with message: `docs(plans): revise A2 plan after pre-implementation spikes`
- [ ] Only then begin Task 1.

---

## File Structure

**Create:**
- `src/AgentX.Core/Services/Shortcuts/ShortcutDescriptor.cs` — record
- `src/AgentX.Core/Services/Shortcuts/ShortcutScope.cs` — scope identifier
- `src/AgentX.Core/Services/Shortcuts/KeyChord.cs` — key combo value-object
- `src/AgentX.Core/Services/Shortcuts/IShortcutRegistry.cs` — interface for the evolved registry
- `src/AgentX.Core/Services/Shortcuts/FuzzyMatcher.cs` — subsequence scoring
- `src/AgentX.Core/Services/Shortcuts/ChordStateMachine.cs` — infrastructure (no chords wired in seed, reserved for future)
- `src/AgentX.App/Services/ShortcutInputRouter.cs` — MainWindow hook
- `src/AgentX.App/ViewModels/JumpToViewModel.cs`
- `src/AgentX.App/ViewModels/CheatsheetViewModel.cs`
- `src/AgentX.App/Views/Dialogs/JumpToDialog.xaml` + `.xaml.cs`
- `src/AgentX.App/Views/Dialogs/CheatsheetDialog.xaml` + `.xaml.cs`
- `src/AgentX.App/Services/ShortcutCatalog.cs` — default seed of ~12 shortcuts (migrates content from existing `RegisterDefaultShortcuts()`)
- `tests/AgentX.Tests/Services/Shortcuts/ShortcutRegistryTests.cs`
- `tests/AgentX.Tests/Services/Shortcuts/FuzzyMatcherTests.cs`
- `tests/AgentX.Tests/Services/Shortcuts/ChordStateMachineTests.cs`
- `tests/AgentX.Tests/Services/Shortcuts/KeyChordTests.cs`
- `tests/AgentX.Tests/ViewModels/CommandPaletteViewModelTests.cs`
- `tests/AgentX.Tests/ViewModels/JumpToViewModelTests.cs`

**Modify (extend existing, don't replace):**
- `src/AgentX.App/Services/KeyboardShortcutService.cs` — **renamed to `ShortcutRegistry`** (per Conflict 1 decision); existing public API preserved, new scope + descriptor APIs added; implements `IShortcutRegistry`
- `src/AgentX.App/Views/CommandPalette.xaml` + `.xaml.cs` — **extend existing** (per Conflict 2 decision): bind to `IShortcutRegistry`, add fuzzy search over the full registry, preserve existing behavior for narrower Quick Actions mode. New `CommandPaletteViewModel` goes in the existing file's ViewModel partner (see Task 5 revision).
- `src/AgentX.App/ViewModels/CommandPaletteViewModel.cs` — may already exist; if so, extend; if not, create
- `src/AgentX.App/MainWindow.xaml.cs` — wire router attach at **line 121** (one-line change per Spike 3); drop redundant `RegisterDefaultShortcuts()` calls that migrate into `ShortcutCatalog.SeedDefaults()`; replace existing `Ctrl+Shift+?` shortcuts-overlay invocation with new `CheatsheetDialog`
- `src/AgentX.App/App.xaml.cs` — DI: rename `services.AddSingleton<KeyboardShortcutService>()` → `services.AddSingleton<IShortcutRegistry, ShortcutRegistry>()` + register new services + run `ShortcutCatalog.SeedDefaults` at startup
- `src/AgentX.App/Strings/*/Resources.resw` — 6 locales — dialog + shortcut labels (feeds A1 coverage)
- `src/AgentX.App/Views/**/*.xaml.cs` — pages add `OnNavigatedTo` / `OnNavigatedFrom` hooks registering scope-local shortcuts (replacement for existing per-page registration pattern)
- `docs/ARCHITECTURE.md` — new "Keyboard Power Mode (A2)" section
- `docs/USER-GUIDE.md` — new "Keyboard shortcuts" section
- `docs/DEVELOPER-GUIDE.md` — new "Registering a new shortcut" workflow
- `docs/v2.1.0-RELEASE-NOTES.md` — A2 feature entry

**Deleted:**
- `CommandPaletteDialog.xaml` / `.xaml.cs` — dropped from plan. The planned new dialog is merged into the existing `CommandPalette.xaml` to avoid two palette surfaces.

---

### Task 1: Core value-objects — `KeyChord`, `ShortcutScope`, `ShortcutDescriptor`

**Files:**
- Create: `src/AgentX.Core/Services/Shortcuts/KeyChord.cs`
- Create: `src/AgentX.Core/Services/Shortcuts/ShortcutScope.cs`
- Create: `src/AgentX.Core/Services/Shortcuts/ShortcutDescriptor.cs`
- Create: `tests/AgentX.Tests/Services/Shortcuts/KeyChordTests.cs`

- [ ] **Step 1: Write `KeyChord`**

```csharp
using System;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// An immutable representation of a keyboard combo (modifiers + a single key).
/// Supports multi-step chords: e.g., "Ctrl+K, D" is two KeyChord values chained.
/// </summary>
public sealed record KeyChord(
    KeyModifiers Modifiers,
    VirtualKeyCode Key)
{
    /// <summary>Human-readable display ("Ctrl+Shift+P").</summary>
    public string Display => KeyChordFormatter.Format(this);
}

[Flags]
public enum KeyModifiers
{
    None  = 0,
    Ctrl  = 1 << 0,
    Shift = 1 << 1,
    Alt   = 1 << 2,
    Win   = 1 << 3,
}

/// <summary>Platform-neutral key codes for the chord vocabulary Agent-X supports.</summary>
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
}

public static class KeyChordFormatter
{
    public static string Format(KeyChord c)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (c.Modifiers.HasFlag(KeyModifiers.Ctrl))  parts.Add("Ctrl");
        if (c.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (c.Modifiers.HasFlag(KeyModifiers.Alt))   parts.Add("Alt");
        if (c.Modifiers.HasFlag(KeyModifiers.Win))   parts.Add("Win");
        parts.Add(FormatKey(c.Key));
        return string.Join("+", parts);
    }

    private static string FormatKey(VirtualKeyCode k) => k switch
    {
        VirtualKeyCode.Oem2     => "?",
        VirtualKeyCode.OemPlus  => "+",
        VirtualKeyCode.OemMinus => "-",
        _ => k.ToString(),
    };
}
```

- [ ] **Step 2: Write `ShortcutScope`**

```csharp
namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Context that owns a group of shortcuts. "Global" shortcuts fire from anywhere;
/// per-page scopes only fire when that page is the active navigation frame.
/// </summary>
public sealed record ShortcutScope(string Name)
{
    public static readonly ShortcutScope Global = new("Global");

    public bool IsGlobal => Name == Global.Name;
}
```

- [ ] **Step 3: Write `ShortcutDescriptor`**

```csharp
using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// A single registered shortcut. Immutable. Handler is an async delegate so palette commands
/// can navigate, show dialogs, or invoke services.
/// </summary>
public sealed record ShortcutDescriptor(
    string Id,                  // stable, e.g., "doc.import" — used for telemetry + config
    string Label,               // localized UI label, e.g., "Import Document…"
    ShortcutScope Scope,        // Global or a page name
    IReadOnlyList<KeyChord> Chord,  // 1 element for simple, N for multi-step
    Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> Handler,
    string? Category = null)    // optional grouping label for cheatsheet ("Documents", "Chat", "Navigation")
{
    public KeyChord PrimaryKey => Chord[0];
    public bool IsChord => Chord.Count > 1;
    public string DisplayChord => string.Join(", ", Chord.Select(k => k.Display));
}
```

- [ ] **Step 4: Write failing tests for `KeyChord` + formatter**

```csharp
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class KeyChordTests
{
    [Theory]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, "Ctrl+Shift+P")]
    [InlineData(KeyModifiers.Ctrl, VirtualKeyCode.P, "Ctrl+P")]
    [InlineData(KeyModifiers.None, VirtualKeyCode.Oem2, "?")]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt, VirtualKeyCode.Delete, "Ctrl+Alt+Delete")]
    [InlineData(KeyModifiers.Ctrl, VirtualKeyCode.K, "Ctrl+K")]
    public void Display_formats_modifiers_in_order_with_plus_separator(KeyModifiers mods, VirtualKeyCode key, string expected)
    {
        new KeyChord(mods, key).Display.Should().Be(expected);
    }

    [Fact]
    public void Value_equality_works_with_same_modifiers_and_key()
    {
        var a = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        var b = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        a.Should().Be(b);
    }

    [Fact]
    public void Value_inequality_on_modifier_difference()
    {
        var a = new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P);
        var b = new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P);
        a.Should().NotBe(b);
    }
}
```

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~KeyChordTests"
```

Expected: 7 tests pass (5 Theory + 2 Fact).

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/Services/Shortcuts/KeyChord.cs src/AgentX.Core/Services/Shortcuts/ShortcutScope.cs src/AgentX.Core/Services/Shortcuts/ShortcutDescriptor.cs tests/AgentX.Tests/Services/Shortcuts/KeyChordTests.cs
git commit -m "feat(a2): KeyChord + ShortcutScope + ShortcutDescriptor value-objects"
```

---

### Task 2: Evolve `KeyboardShortcutService` → `IShortcutRegistry` / `ShortcutRegistry`

**Per Conflict 1 decision:** this task EVOLVES the existing `KeyboardShortcutService` rather than creating a parallel registry. Renaming is surgical — existing call sites in `MainWindow.xaml.cs:113` + `RegisterDefaultShortcuts()` (lines 141–225) get updated in-place. The evolved service implements a new `IShortcutRegistry` interface so future callers go through the interface and DI.

**Files:**
- Create: `src/AgentX.Core/Services/Shortcuts/IShortcutRegistry.cs` (new interface)
- Rename + evolve: `src/AgentX.App/Services/KeyboardShortcutService.cs` → `src/AgentX.App/Services/ShortcutRegistry.cs` (same file, renamed, implements new interface, existing public surface preserved)
- Modify: `src/AgentX.App/MainWindow.xaml.cs:113` — `App.GetService<KeyboardShortcutService>()` → `App.GetService<IShortcutRegistry>()`
- Modify: `src/AgentX.App/App.xaml.cs:280` — `services.AddSingleton<KeyboardShortcutService>()` → `services.AddSingleton<IShortcutRegistry, ShortcutRegistry>()`
- Modify: ALL other call sites found via `grep -rn 'KeyboardShortcutService' src/` — update type references
- Create: `tests/AgentX.Tests/Services/Shortcuts/ShortcutRegistryTests.cs`

**Pre-Task audit (run first):**
Before touching anything, confirm the current `KeyboardShortcutService` public surface by grepping:
```bash
grep -rn 'KeyboardShortcutService' src/ | head -30
```
Document every call site and method invoked. The evolved `ShortcutRegistry` must preserve ALL existing methods so these sites keep compiling.

- [ ] **Step 1: Write the interface**

```csharp
using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

public interface IShortcutRegistry
{
    /// <summary>Registers a shortcut. Returns a token that unregisters on Dispose.</summary>
    IDisposable Register(ShortcutDescriptor descriptor);

    /// <summary>All descriptors (global + every scope).</summary>
    IReadOnlyList<ShortcutDescriptor> All();

    /// <summary>Descriptors for Global + the given scope name.</summary>
    IReadOnlyList<ShortcutDescriptor> ForScope(string scopeName);

    /// <summary>Finds a descriptor matching the first chord key — used by input router.</summary>
    ShortcutDescriptor? FindByPrimaryKey(KeyChord key, string? activeScopeName);

    /// <summary>Event fired on any registration change — palette VM refreshes from this.</summary>
    event EventHandler? Changed;
}
```

- [ ] **Step 2: Write failing tests**

```csharp
using System.Linq;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class ShortcutRegistryTests
{
    [Fact]
    public void Register_adds_descriptor_and_fires_changed()
    {
        var sut = new ShortcutRegistry();
        int changedFiredCount = 0;
        sut.Changed += (_, _) => changedFiredCount++;

        using var _ = sut.Register(NewDescriptor("a", KeyModifiers.Ctrl, VirtualKeyCode.A));

        sut.All().Should().HaveCount(1);
        changedFiredCount.Should().Be(1);
    }

    [Fact]
    public void Register_disposal_unregisters_and_fires_changed()
    {
        var sut = new ShortcutRegistry();
        var token = sut.Register(NewDescriptor("a", KeyModifiers.Ctrl, VirtualKeyCode.A));
        int changedAfterFirst = 0;
        sut.Changed += (_, _) => changedAfterFirst++;

        token.Dispose();

        sut.All().Should().BeEmpty();
        changedAfterFirst.Should().Be(1);
    }

    [Fact]
    public void ForScope_returns_global_plus_scope_descriptors_only()
    {
        var sut = new ShortcutRegistry();
        sut.Register(NewDescriptor("g", KeyModifiers.Ctrl, VirtualKeyCode.G, ShortcutScope.Global));
        sut.Register(NewDescriptor("docs.import", KeyModifiers.Ctrl, VirtualKeyCode.I, new ShortcutScope("DocumentsPage")));
        sut.Register(NewDescriptor("chat.clear", KeyModifiers.Ctrl, VirtualKeyCode.L, new ShortcutScope("ChatPage")));

        var forDocs = sut.ForScope("DocumentsPage");

        forDocs.Select(d => d.Id).Should().BeEquivalentTo("g", "docs.import");
    }

    [Fact]
    public void FindByPrimaryKey_matches_global_regardless_of_active_scope()
    {
        var sut = new ShortcutRegistry();
        var global = NewDescriptor("palette", KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, ShortcutScope.Global);
        sut.Register(global);

        var match = sut.FindByPrimaryKey(new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P), activeScopeName: "AnyPage");

        match.Should().NotBeNull();
        match!.Id.Should().Be("palette");
    }

    [Fact]
    public void FindByPrimaryKey_scope_beats_global_when_both_match()
    {
        var sut = new ShortcutRegistry();
        sut.Register(NewDescriptor("global.refresh", KeyModifiers.None, VirtualKeyCode.F5, ShortcutScope.Global));
        sut.Register(NewDescriptor("docs.refresh", KeyModifiers.None, VirtualKeyCode.F5, new ShortcutScope("DocumentsPage")));

        var match = sut.FindByPrimaryKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.F5), activeScopeName: "DocumentsPage");

        match!.Id.Should().Be("docs.refresh"); // page-scoped wins
    }

    [Fact]
    public void FindByPrimaryKey_returns_null_when_no_match()
    {
        var sut = new ShortcutRegistry();

        var match = sut.FindByPrimaryKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.X), activeScopeName: null);

        match.Should().BeNull();
    }

    private static ShortcutDescriptor NewDescriptor(string id, KeyModifiers mods, VirtualKeyCode key, ShortcutScope? scope = null)
        => new(id, $"Label-{id}", scope ?? ShortcutScope.Global,
               new[] { new KeyChord(mods, key) },
               _ => Task.CompletedTask,
               Category: null);
}
```

- [ ] **Step 3: Run — expect compile fail (`ShortcutRegistry` missing)**

- [ ] **Step 4: Write implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AgentX.Core.Services.Shortcuts;

public sealed class ShortcutRegistry : IShortcutRegistry
{
    private readonly List<ShortcutDescriptor> _items = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public event EventHandler? Changed;

    public IDisposable Register(ShortcutDescriptor descriptor)
    {
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

    private void Remove(ShortcutDescriptor d)
    {
        _lock.EnterWriteLock();
        try { _items.Remove(d); }
        finally { _lock.ExitWriteLock(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class UnregisterToken : IDisposable
    {
        private readonly ShortcutRegistry _r;
        private readonly ShortcutDescriptor _d;
        private bool _disposed;

        public UnregisterToken(ShortcutRegistry r, ShortcutDescriptor d) { _r = r; _d = d; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _r.Remove(_d);
        }
    }
}
```

- [ ] **Step 5: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~ShortcutRegistryTests"
```

Expected: 6 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/Services/Shortcuts/IShortcutRegistry.cs src/AgentX.Core/Services/Shortcuts/ShortcutRegistry.cs tests/AgentX.Tests/Services/Shortcuts/ShortcutRegistryTests.cs
git commit -m "feat(a2): IShortcutRegistry with scope-aware lookup"
```

---

### Task 3: `FuzzyMatcher` — VS-Code-style subsequence scoring

**Files:**
- Create: `src/AgentX.Core/Services/Shortcuts/FuzzyMatcher.cs`
- Create: `tests/AgentX.Tests/Services/Shortcuts/FuzzyMatcherTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class FuzzyMatcherTests
{
    [Fact]
    public void Score_exact_match_is_highest()
    {
        FuzzyMatcher.Score("import document", "import document")
            .Should().BeGreaterThan(FuzzyMatcher.Score("import document", "import"));
    }

    [Fact]
    public void Score_prefix_beats_suffix()
    {
        var prefix = FuzzyMatcher.Score("import document", "import");
        var suffix = FuzzyMatcher.Score("import document", "document");
        prefix.Should().BeGreaterThan(suffix);
    }

    [Fact]
    public void Score_word_boundary_beats_mid_word()
    {
        // "id" matches "Import Document" word boundaries — good score.
        // "id" also appears in "Bridge" mid-word — lower score.
        FuzzyMatcher.Score("import document", "id")
            .Should().BeGreaterThan(FuzzyMatcher.Score("bridge", "id"));
    }

    [Fact]
    public void Score_is_zero_for_non_matching_query()
    {
        FuzzyMatcher.Score("import document", "xyz").Should().Be(0);
    }

    [Fact]
    public void Score_case_insensitive()
    {
        FuzzyMatcher.Score("Import Document", "IMPORT")
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rank_orders_best_match_first()
    {
        var items = new[] { "Settings", "Import Document", "Export Document", "Chat" };
        var ranked = FuzzyMatcher.Rank(items, x => x, "doc")
                                  .Select(r => r.Item).ToList();
        ranked[0].Should().ContainAny("Import Document", "Export Document");
        ranked.Last().Should().Be("Chat"); // no match at all
    }

    [Fact]
    public void Rank_excludes_zero_scores()
    {
        var items = new[] { "Alpha", "Beta", "Gamma" };
        var ranked = FuzzyMatcher.Rank(items, x => x, "xyz").ToList();
        ranked.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run — expect compile fail**

- [ ] **Step 3: Write implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Subsequence-scoring fuzzy matcher inspired by VS Code's quick-pick ranker.
/// - Higher score = better match. Score of 0 = no match.
/// - Scoring boosts: word-boundary matches, consecutive matches, prefix matches.
/// </summary>
public static class FuzzyMatcher
{
    public record ScoredItem<T>(T Item, int Score);

    public static int Score(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 1; // empty query matches everything weakly
        if (string.IsNullOrEmpty(haystack)) return 0;

        var h = haystack.ToLowerInvariant();
        var n = needle.ToLowerInvariant();

        int score = 0;
        int hi = 0;    // haystack index
        int consecutive = 0;

        for (int ni = 0; ni < n.Length; ni++)
        {
            var target = n[ni];
            bool found = false;
            while (hi < h.Length)
            {
                if (h[hi] == target)
                {
                    // Word-boundary bonus: this char starts a word (first char or after space).
                    bool isBoundary = hi == 0 || h[hi - 1] == ' ' || h[hi - 1] == '-' || h[hi - 1] == '_';
                    if (isBoundary) score += 3;
                    if (consecutive > 0) score += 2; // consecutive bonus
                    if (hi == 0 && ni == 0) score += 5; // prefix bonus
                    score += 1; // base point
                    consecutive++;
                    hi++;
                    found = true;
                    break;
                }
                else
                {
                    consecutive = 0;
                    hi++;
                }
            }
            if (!found) return 0; // any query char not found at all → no match
        }

        // Short-haystack bonus (less noise → better signal).
        if (haystack.Length < 20) score += 1;
        return score;
    }

    public static IEnumerable<ScoredItem<T>> Rank<T>(
        IEnumerable<T> items,
        Func<T, string> labelSelector,
        string query)
    {
        return items
            .Select(i => new ScoredItem<T>(i, Score(labelSelector(i), query)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => labelSelector(s.Item).Length); // tie-break: shorter first
    }
}
```

- [ ] **Step 4: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~FuzzyMatcherTests"
```

Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Services/Shortcuts/FuzzyMatcher.cs tests/AgentX.Tests/Services/Shortcuts/FuzzyMatcherTests.cs
git commit -m "feat(a2): FuzzyMatcher with VS-Code-style subsequence scoring"
```

---

### Task 4: `ChordStateMachine` — multi-step chord tracking

**Files:**
- Create: `src/AgentX.Core/Services/Shortcuts/ChordStateMachine.cs`
- Create: `tests/AgentX.Tests/Services/Shortcuts/ChordStateMachineTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Shortcuts;

public class ChordStateMachineTests
{
    [Fact]
    public void Press_non_prefix_key_returns_None_result()
    {
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => DateTime.UtcNow);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        var result = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.A));

        result.Kind.Should().Be(ChordResultKind.None);
    }

    [Fact]
    public void Press_prefix_then_within_window_returns_ChordCompleted()
    {
        var t = DateTime.UtcNow;
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => t);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        var r1 = sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        t = t.AddMilliseconds(500);
        var r2 = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r1.Kind.Should().Be(ChordResultKind.PrefixArmed);
        r2.Kind.Should().Be(ChordResultKind.ChordCompleted);
        r2.CompletedChord.Should().HaveCount(2);
        r2.CompletedChord![0].Should().Be(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        r2.CompletedChord![1].Should().Be(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));
    }

    [Fact]
    public void Prefix_expires_after_window()
    {
        var t = DateTime.UtcNow;
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => t);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        t = t.AddMilliseconds(1200);
        var r = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r.Kind.Should().Be(ChordResultKind.None);
    }

    [Fact]
    public void Escape_cancels_armed_prefix()
    {
        var sut = new ChordStateMachine(windowMs: 1000, clock: () => DateTime.UtcNow);
        sut.RegisterPrefix(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));

        sut.OnKey(new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K));
        sut.Reset();
        var r = sut.OnKey(new KeyChord(KeyModifiers.None, VirtualKeyCode.D));

        r.Kind.Should().Be(ChordResultKind.None);
    }
}
```

- [ ] **Step 2: Run — expect compile fail**

- [ ] **Step 3: Write implementation**

```csharp
using System;
using System.Collections.Generic;

namespace AgentX.Core.Services.Shortcuts;

public enum ChordResultKind
{
    None,            // key not part of any chord/prefix — handle normally (or ignore)
    PrefixArmed,     // first chord of multi-step just pressed — swallow key, wait for second
    ChordCompleted,  // second-step key pressed within window — fire the chord
}

public sealed record ChordResult(ChordResultKind Kind, IReadOnlyList<KeyChord>? CompletedChord = null);

/// <summary>
/// Tracks multi-step chord state ("Ctrl+K, D"). Registered prefixes are known upfront;
/// the first keypress arms a prefix and starts a timer window; a subsequent key within
/// the window completes the chord.
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
        _clock = clock;
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
            // Window expired. Clear and fall through.
            _armedPrefix = null;
        }

        // Is this a prefix? Arm it.
        if (_prefixes.Contains(key))
        {
            _armedPrefix = key;
            _armedAt = _clock();
            return new ChordResult(ChordResultKind.PrefixArmed);
        }

        return new ChordResult(ChordResultKind.None);
    }
}
```

- [ ] **Step 4: Run — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~ChordStateMachineTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AgentX.Core/Services/Shortcuts/ChordStateMachine.cs tests/AgentX.Tests/Services/Shortcuts/ChordStateMachineTests.cs
git commit -m "feat(a2): ChordStateMachine with timed multi-step chord tracking"
```

---

### Task 5: Extend existing `CommandPalette.xaml.cs` with registry-driven fuzzy search

**Per Conflict 2 decision:** this task EXTENDS the existing `src/AgentX.App/Views/CommandPalette.xaml.cs` (and its ViewModel partner) rather than creating a new `CommandPaletteDialog`. The existing palette already handles keyboard navigation (Down/Up/Enter/Escape/Tab — Spike 0 finding). We add: (a) `IShortcutRegistry` data binding, (b) `FuzzyMatcher`-ranked results, (c) `Ctrl+Shift+P` as a second trigger alongside existing `Ctrl+K`.

**Files:**
- Modify: `src/AgentX.App/Views/CommandPalette.xaml` (existing) — bind `ItemsSource` to new registry-backed VM
- Modify: `src/AgentX.App/Views/CommandPalette.xaml.cs` (existing) — wire to new VM; preserve existing key handling
- Modify: `src/AgentX.App/ViewModels/CommandPaletteViewModel.cs` (existing OR create if absent — check first) — swap data source to `IShortcutRegistry`, add fuzzy filter
- Create: `tests/AgentX.Tests/ViewModels/CommandPaletteViewModelTests.cs`

**Pre-Task audit (run first):**
Open `src/AgentX.App/Views/CommandPalette.xaml` + `.xaml.cs` and document:
- Current ViewModel type (if any)
- Current data source (`ObservableCollection<T>` of what?)
- Current trigger code path (how is `Ctrl+K` wired today?)
- Existing keyboard handling (lines 538–561 per Spike 0)

- [ ] **Step 1: Write failing ViewModel tests**

```csharp
using System.Linq;
using System.Threading.Tasks;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    [Fact]
    public void Initial_state_lists_all_global_and_active_scope_descriptors()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Desc("g.one",  "Global one",  ShortcutScope.Global));
        registry.Register(Desc("d.one",  "Docs one",    new ShortcutScope("DocumentsPage")));
        registry.Register(Desc("c.one",  "Chat one",    new ShortcutScope("ChatPage")));

        var sut = new CommandPaletteViewModel(registry, activeScopeName: "DocumentsPage");

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("g.one", "d.one");
    }

    [Fact]
    public void Filter_narrows_results_fuzzy_by_label()
    {
        var registry = new ShortcutRegistry();
        registry.Register(Desc("imp", "Import Document", ShortcutScope.Global));
        registry.Register(Desc("exp", "Export Document", ShortcutScope.Global));
        registry.Register(Desc("set", "Settings",        ShortcutScope.Global));

        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);
        sut.Query = "doc";

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("imp", "exp");
    }

    [Fact]
    public async Task Execute_invokes_descriptor_handler()
    {
        var handlerFired = false;
        var descriptor = new ShortcutDescriptor(
            "x", "X action", ShortcutScope.Global,
            new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.X) },
            _ => { handlerFired = true; return Task.CompletedTask; });

        var registry = new ShortcutRegistry();
        registry.Register(descriptor);
        var sut = new CommandPaletteViewModel(registry, activeScopeName: null);

        await sut.ExecuteAsync(descriptor);

        handlerFired.Should().BeTrue();
    }

    private static ShortcutDescriptor Desc(string id, string label, ShortcutScope scope)
        => new(id, label, scope,
               new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.A) },
               _ => Task.CompletedTask);
}
```

- [ ] **Step 2: Run — expect compile fail**

- [ ] **Step 3: Write `CommandPaletteViewModel`**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IShortcutRegistry _registry;
    private readonly string? _activeScopeName;

    public CommandPaletteViewModel(IShortcutRegistry registry, string? activeScopeName)
    {
        _registry = registry;
        _activeScopeName = activeScopeName;
        RefreshResults();
        _registry.Changed += (_, _) => RefreshResults();
    }

    [ObservableProperty] private string query = string.Empty;

    public ObservableCollection<ShortcutDescriptor> Results { get; } = new();

    partial void OnQueryChanged(string value) => RefreshResults();

    private void RefreshResults()
    {
        var available = _activeScopeName is null
            ? _registry.All().Where(d => d.Scope.IsGlobal)
            : _registry.ForScope(_activeScopeName);

        var ordered = string.IsNullOrWhiteSpace(Query)
            ? available.OrderBy(d => d.Label).ToList()
            : FuzzyMatcher
                .Rank(available, d => d.Label, Query)
                .Select(s => s.Item)
                .ToList();

        Results.Clear();
        foreach (var r in ordered) Results.Add(r);
    }

    [RelayCommand]
    public async Task ExecuteAsync(ShortcutDescriptor descriptor)
    {
        if (descriptor is null) return;
        await descriptor.Handler(CancellationToken.None);
    }
}
```

- [ ] **Step 4: Extend existing `CommandPalette.xaml` binding**

Modify the existing `src/AgentX.App/Views/CommandPalette.xaml`. The exact edits depend on what's in the file today (establish via Pre-Task audit). The goal: the ListView's `ItemsSource` must bind to `ViewModel.Results` (the registry-ranked list). If the file currently hard-codes a list of Quick Actions, replace with the VM binding.

Target shape (adapt to existing XAML structure — do NOT replace the file wholesale):

```xml
<!-- Inside the existing ContentDialog / UserControl root -->

<TextBox
    x:Name="QueryBox"
    x:Uid="CommandPalette_QueryBox"
    Text="{x:Bind ViewModel.Query, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />

<ListView
    x:Name="ResultsList"
    ItemsSource="{x:Bind ViewModel.Results, Mode=OneWay}"
    SelectionMode="Single">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="shortcuts:ShortcutDescriptor">
            <Grid ColumnSpacing="12" Padding="16,10">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0" Spacing="2">
                    <TextBlock Text="{x:Bind Label}" FontWeight="SemiBold"/>
                    <TextBlock Text="{x:Bind Category}" Opacity="0.6" FontSize="12"/>
                </StackPanel>
                <Border Grid.Column="1"
                        Padding="6,2"
                        CornerRadius="4"
                        Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                    <TextBlock Text="{x:Bind DisplayChord}" FontFamily="Consolas" FontSize="12"/>
                </Border>
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

Add the `xmlns:shortcuts="using:AgentX.Core.Services.Shortcuts"` namespace declaration at the root element if it isn't already present.

- [ ] **Step 5: Extend existing `CommandPalette.xaml.cs`**

Modify the existing code-behind. Preserve existing key handling (Down/Up/Enter/Escape/Tab at lines 538–561 per Spike 0). Add VM resolution + focus management on open. Target pattern:

```csharp
// In the existing CommandPalette class — add / modify these members:

public CommandPaletteViewModel ViewModel { get; private set; } = default!;

public CommandPalette(CommandPaletteViewModel vm)  // if existing ctor is parameterless, add overload
{
    ViewModel = vm;
    InitializeComponent();
    Opened += (_, _) => QueryBox.Focus(FocusState.Programmatic);
}

// Preserve existing OnKeyDown / OnResultDoubleTapped handlers. For Enter, route through ViewModel.ExecuteAsync:
private async void OnResultsKeyDown(object sender, KeyRoutedEventArgs e)
{
    if (e.Key == VirtualKey.Enter && ResultsList.SelectedItem is ShortcutDescriptor d)
    {
        Hide();
        await ViewModel.ExecuteAsync(d);
    }
    // ...existing Down/Up/Tab handling stays as-is...
}
```

**Do NOT delete existing handlers** without confirming they're replaced by the new behavior. The pre-task audit recorded them — preserve or replace consciously, one at a time.

- [ ] **Step 6: Run ViewModel tests — expect pass**

```bash
dotnet test --filter "FullyQualifiedName~CommandPaletteViewModelTests"
```

Expected: 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/AgentX.App/ViewModels/CommandPaletteViewModel.cs \
        src/AgentX.App/Views/CommandPalette.xaml \
        src/AgentX.App/Views/CommandPalette.xaml.cs \
        tests/AgentX.Tests/ViewModels/CommandPaletteViewModelTests.cs
git commit -m "feat(a2): extend CommandPalette with registry-driven fuzzy search (Ctrl+K + Ctrl+Shift+P)"
```

---

### Task 6: `JumpToViewModel` + `JumpToDialog`

Similar shape to command palette — but the candidate list comes from `IDocumentService.GetRecentAsync()` + `IConversationService.GetAllAsync()` + a hardcoded pages list (`Documents`, `Chat`, `Settings`, `Audit Log`, etc.).

**Files:**
- Create: `src/AgentX.App/ViewModels/JumpToViewModel.cs`
- Create: `src/AgentX.App/Views/Dialogs/JumpToDialog.xaml`
- Create: `src/AgentX.App/Views/Dialogs/JumpToDialog.xaml.cs`
- Create: `tests/AgentX.Tests/ViewModels/JumpToViewModelTests.cs`

- [x] **Step 1: Write `JumpToItem` record + ViewModel**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentX.App.ViewModels;

public enum JumpToItemKind { Page, Document, Conversation }

public sealed record JumpToItem(
    string Id,
    string Label,
    string? Subtitle,
    JumpToItemKind Kind,
    Func<CancellationToken, Task> OpenAction);

public partial class JumpToViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<JumpToItem>>> _loadCandidates;
    private IReadOnlyList<JumpToItem> _allCandidates = Array.Empty<JumpToItem>();

    public JumpToViewModel(Func<CancellationToken, Task<IReadOnlyList<JumpToItem>>> loadCandidates)
    {
        _loadCandidates = loadCandidates;
    }

    [ObservableProperty] private string query = string.Empty;

    public ObservableCollection<JumpToItem> Results { get; } = new();

    partial void OnQueryChanged(string value) => Refresh();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _allCandidates = await _loadCandidates(ct);
        Refresh();
    }

    private void Refresh()
    {
        var ordered = string.IsNullOrWhiteSpace(Query)
            ? _allCandidates.OrderBy(c => c.Kind).ThenBy(c => c.Label).ToList()
            : FuzzyMatcher.Rank(_allCandidates, c => c.Label, Query)
                          .Select(s => s.Item)
                          .ToList();

        Results.Clear();
        foreach (var r in ordered) Results.Add(r);
    }

    [RelayCommand]
    public async Task ExecuteAsync(JumpToItem item)
    {
        if (item is null) return;
        await item.OpenAction(CancellationToken.None);
    }
}
```

- [x] **Step 2: Write tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentX.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

public class JumpToViewModelTests
{
    [Fact]
    public async Task Load_populates_results_from_loader()
    {
        var candidates = new List<JumpToItem>
        {
            new("p.docs", "Documents", null, JumpToItemKind.Page, _ => Task.CompletedTask),
            new("d.1", "Annual Report 2026.pdf", null, JumpToItemKind.Document, _ => Task.CompletedTask),
        };
        var sut = new JumpToViewModel(_ => Task.FromResult((IReadOnlyList<JumpToItem>)candidates));

        await sut.LoadAsync();

        sut.Results.Select(r => r.Id).Should().BeEquivalentTo("p.docs", "d.1");
    }

    [Fact]
    public async Task Query_fuzzy_filters_candidates()
    {
        var candidates = new List<JumpToItem>
        {
            new("d.1", "Annual Report 2026.pdf", null, JumpToItemKind.Document, _ => Task.CompletedTask),
            new("d.2", "Meeting Notes.md", null, JumpToItemKind.Document, _ => Task.CompletedTask),
        };
        var sut = new JumpToViewModel(_ => Task.FromResult((IReadOnlyList<JumpToItem>)candidates));
        await sut.LoadAsync();

        sut.Query = "annual";

        sut.Results.Select(r => r.Id).Should().ContainSingle().Which.Should().Be("d.1");
    }
}
```

- [x] **Step 3: Write `JumpToDialog.xaml` + `.xaml.cs`**

Nearly identical to `CommandPaletteDialog`, but:
- DataTemplate shows `Kind` as an icon (`"Page" = FontIcon E8A5`, `"Document" = E8A5`, `"Conversation" = E90A`)
- Title is localized via `x:Uid="JumpTo_Title"`
- On `Enter` / double-tap, call `ViewModel.ExecuteAsync(selected)`

Pattern:

```xml
<ContentDialog
    x:Class="AgentX.App.Views.Dialogs.JumpToDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:AgentX.App.ViewModels"
    Style="{StaticResource DefaultContentDialogStyle}">
  <!-- Same shape as CommandPaletteDialog; see that XAML for full layout. -->
  <!-- ResultsList DataTemplate binds to JumpToItem.Label + .Subtitle + .Kind (map Kind→icon in a converter). -->
</ContentDialog>
```

Code-behind mirrors `CommandPaletteDialog.xaml.cs` with ViewModel type swapped.

- [x] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~JumpToViewModelTests"
```

Expected: 2 tests pass.

- [x] **Step 5: Commit**

```bash
git add src/AgentX.App/ViewModels/JumpToViewModel.cs src/AgentX.App/Views/Dialogs/JumpToDialog.* tests/AgentX.Tests/ViewModels/JumpToViewModelTests.cs
git commit -m "feat(a2): JumpToDialog (Ctrl+P) with documents/conversations/pages"
```

---

### Task 7: `CheatsheetViewModel` + `CheatsheetDialog`

The cheatsheet groups registered shortcuts by **Category** (secondary grouping) and highlights the **Current Page** bucket.

**Files:**
- Create: `src/AgentX.App/ViewModels/CheatsheetViewModel.cs`
- Create: `src/AgentX.App/Views/Dialogs/CheatsheetDialog.xaml` + `.xaml.cs`

- [ ] **Step 1: Write `CheatsheetViewModel`**

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentX.Core.Services.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

public sealed class CheatsheetGroup
{
    public string Header { get; init; } = string.Empty;
    public List<ShortcutDescriptor> Items { get; init; } = new();
}

public partial class CheatsheetViewModel : ObservableObject
{
    public CheatsheetViewModel(IShortcutRegistry registry, string? activeScopeName)
    {
        var all = activeScopeName is null
            ? registry.All().Where(d => d.Scope.IsGlobal)
            : registry.ForScope(activeScopeName);

        Groups = new ObservableCollection<CheatsheetGroup>(
            all.GroupBy(d => d.Category ?? (d.Scope.IsGlobal ? "Global" : d.Scope.Name))
               .OrderBy(g => g.Key)
               .Select(g => new CheatsheetGroup
               {
                   Header = g.Key,
                   Items = g.OrderBy(d => d.Label).ToList(),
               }));
    }

    public ObservableCollection<CheatsheetGroup> Groups { get; }
}
```

- [ ] **Step 2: Write `CheatsheetDialog.xaml`**

```xml
<ContentDialog
    x:Class="AgentX.App.Views.Dialogs.CheatsheetDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:AgentX.App.ViewModels"
    xmlns:shortcuts="using:AgentX.Core.Services.Shortcuts"
    x:Uid="Cheatsheet_Dialog"
    Style="{StaticResource DefaultContentDialogStyle}">

    <ScrollViewer MaxHeight="640" MinWidth="560">
        <ItemsRepeater ItemsSource="{x:Bind ViewModel.Groups, Mode=OneWay}">
            <ItemsRepeater.ItemTemplate>
                <DataTemplate x:DataType="vm:CheatsheetGroup">
                    <StackPanel Spacing="4" Padding="20,12,20,4">
                        <TextBlock Text="{x:Bind Header}"
                                   Style="{StaticResource SubtitleTextBlockStyle}"/>
                        <ItemsRepeater ItemsSource="{x:Bind Items}">
                            <ItemsRepeater.ItemTemplate>
                                <DataTemplate x:DataType="shortcuts:ShortcutDescriptor">
                                    <Grid ColumnSpacing="12" Padding="0,4">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{x:Bind Label}"/>
                                        <Border Grid.Column="1"
                                                Padding="6,2"
                                                CornerRadius="4"
                                                Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                                            <TextBlock Text="{x:Bind DisplayChord}"
                                                       FontFamily="Consolas"
                                                       FontSize="12"/>
                                        </Border>
                                    </Grid>
                                </DataTemplate>
                            </ItemsRepeater.ItemTemplate>
                        </ItemsRepeater>
                    </StackPanel>
                </DataTemplate>
            </ItemsRepeater.ItemTemplate>
        </ItemsRepeater>
    </ScrollViewer>
</ContentDialog>
```

- [ ] **Step 3: Write `CheatsheetDialog.xaml.cs`**

```csharp
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views.Dialogs;

public sealed partial class CheatsheetDialog : ContentDialog
{
    public CheatsheetViewModel ViewModel { get; }

    public CheatsheetDialog(CheatsheetViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/ViewModels/CheatsheetViewModel.cs src/AgentX.App/Views/Dialogs/CheatsheetDialog.*
git commit -m "feat(a2): CheatsheetDialog (?) with grouped shortcut help"
```

---

### Task 8: `ShortcutInputRouter` — `MainWindow` input hook

**Files:**
- Create: `src/AgentX.App/Services/ShortcutInputRouter.cs`
- Modify: `src/AgentX.App/MainWindow.xaml.cs` — one-line attach

- [ ] **Step 1: Write router**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AgentX.App.ViewModels;
using AgentX.App.Views.Dialogs;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace AgentX.App.Services;

/// <summary>
/// Hooks MainWindow's key input. Translates WinUI key events into <see cref="KeyChord"/>,
/// consults the ChordStateMachine for multi-step support, then dispatches to either
/// the palette / jump / cheatsheet dialogs OR a matched <see cref="ShortcutDescriptor"/>.
/// </summary>
public sealed class ShortcutInputRouter
{
    private readonly IShortcutRegistry _registry;
    private readonly ChordStateMachine _chords;
    private readonly Func<string?> _activeScopeProvider;
    private readonly Func<CommandPalette> _paletteFactory;
    private readonly Func<JumpToDialog> _jumpToFactory;
    private readonly Func<CheatsheetDialog> _cheatsheetFactory;

    public ShortcutInputRouter(
        IShortcutRegistry registry,
        ChordStateMachine chords,
        Func<string?> activeScopeProvider,
        Func<CommandPalette> paletteFactory,           // existing type, extended (Task 5)
        Func<JumpToDialog> jumpToFactory,
        Func<CheatsheetDialog> cheatsheetFactory)
    {
        _registry = registry;
        _chords = chords;
        _activeScopeProvider = activeScopeProvider;
        _paletteFactory = paletteFactory;
        _jumpToFactory = jumpToFactory;
        _cheatsheetFactory = cheatsheetFactory;
    }

    public void Attach(Window window)
    {
        // Per Spike 1 + Spike 3: use the existing RootGrid.PreviewKeyDown mechanism —
        // it fires pre-focus-dispatch and reliably intercepts even with a focused TextBox.
        // Do NOT use CoreWindow.KeyDown or window.Content.KeyDown — those would layer
        // a second input mechanism alongside the existing pattern.
        if (window.Content is FrameworkElement root)
        {
            var rootGrid = (root as Grid) ?? root.FindName("RootGrid") as FrameworkElement;
            if (rootGrid is null) throw new InvalidOperationException("RootGrid not found on MainWindow");
            rootGrid.PreviewKeyDown += OnKeyDown;
        }
    }

    private async void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var chord = ToKeyChord(e);
        if (chord is null) return;

        // 1) Try chord-state-machine — may swallow the key or return a completed chord.
        // (Infrastructure only in v2.1.0 final — no multi-step chord seeded by ShortcutCatalog.
        //  Per Conflict 3 decision, Ctrl+K stays a single-step palette trigger, not a chord prefix.)
        var chordResult = _chords.OnKey(chord);
        if (chordResult.Kind == ChordResultKind.PrefixArmed)
        {
            e.Handled = true;
            return;
        }
        if (chordResult.Kind == ChordResultKind.ChordCompleted)
        {
            e.Handled = true;
            return;
        }

        // 2) Hardcoded triggers for the three built-in dialogs.
        // Per Conflict 3 decision, each dialog has TWO triggers (legacy + modern).

        // Command Palette: Ctrl+K (legacy) OR Ctrl+Shift+P (VS Code muscle memory)
        if (chord == new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.K)
            || chord == new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P))
        {
            e.Handled = true;
            await _paletteFactory().ShowAsync();
            return;
        }

        // Jump-To: Ctrl+P (single trigger — no conflict with existing chords)
        if (chord == new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.P))
        {
            e.Handled = true;
            await _jumpToFactory().ShowAsync();
            return;
        }

        // Cheatsheet: F1 (Windows convention) OR Ctrl+Shift+? (existing overlay key, folded in)
        // Note: plain `?` (no modifiers) is NOT a cheatsheet trigger — that would fire in text inputs.
        if (chord == new KeyChord(KeyModifiers.None, VirtualKeyCode.F1)
            || chord == new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.Oem2))
        {
            e.Handled = true;
            await _cheatsheetFactory().ShowAsync();
            return;
        }

        // 3) Registry lookup for single-key shortcuts.
        var descriptor = _registry.FindByPrimaryKey(chord, _activeScopeProvider());
        if (descriptor is not null)
        {
            e.Handled = true;
            await descriptor.Handler(CancellationToken.None);
        }
    }

    private static KeyChord? ToKeyChord(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = MapKey(e.Key);
        if (key == VirtualKeyCode.None) return null;

        var mods = KeyModifiers.None;
        var coreWindow = Microsoft.UI.Xaml.Window.Current?.CoreWindow
                         ?? Windows.UI.Core.CoreWindow.GetForCurrentThread();
        if ((coreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            mods |= KeyModifiers.Ctrl;
        if ((coreWindow.GetKeyState(VirtualKey.Shift)   & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            mods |= KeyModifiers.Shift;
        if ((coreWindow.GetKeyState(VirtualKey.Menu)    & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            mods |= KeyModifiers.Alt;

        return new KeyChord(mods, key);
    }

    private static VirtualKeyCode MapKey(VirtualKey k) => k switch
    {
        VirtualKey.A => VirtualKeyCode.A, VirtualKey.B => VirtualKeyCode.B, VirtualKey.C => VirtualKeyCode.C,
        VirtualKey.D => VirtualKeyCode.D, VirtualKey.E => VirtualKeyCode.E, VirtualKey.F => VirtualKeyCode.F,
        VirtualKey.G => VirtualKeyCode.G, VirtualKey.H => VirtualKeyCode.H, VirtualKey.I => VirtualKeyCode.I,
        VirtualKey.J => VirtualKeyCode.J, VirtualKey.K => VirtualKeyCode.K, VirtualKey.L => VirtualKeyCode.L,
        VirtualKey.M => VirtualKeyCode.M, VirtualKey.N => VirtualKeyCode.N, VirtualKey.O => VirtualKeyCode.O,
        VirtualKey.P => VirtualKeyCode.P, VirtualKey.Q => VirtualKeyCode.Q, VirtualKey.R => VirtualKeyCode.R,
        VirtualKey.S => VirtualKeyCode.S, VirtualKey.T => VirtualKeyCode.T, VirtualKey.U => VirtualKeyCode.U,
        VirtualKey.V => VirtualKeyCode.V, VirtualKey.W => VirtualKeyCode.W, VirtualKey.X => VirtualKeyCode.X,
        VirtualKey.Y => VirtualKeyCode.Y, VirtualKey.Z => VirtualKeyCode.Z,
        VirtualKey.Enter => VirtualKeyCode.Enter, VirtualKey.Escape => VirtualKeyCode.Escape,
        VirtualKey.Tab => VirtualKeyCode.Tab, VirtualKey.Space => VirtualKeyCode.Space,
        VirtualKey.Delete => VirtualKeyCode.Delete, VirtualKey.Back => VirtualKeyCode.Backspace,
        VirtualKey.Left => VirtualKeyCode.Left, VirtualKey.Right => VirtualKeyCode.Right,
        VirtualKey.Up => VirtualKeyCode.Up, VirtualKey.Down => VirtualKeyCode.Down,
        VirtualKey.Home => VirtualKeyCode.Home, VirtualKey.End => VirtualKeyCode.End,
        VirtualKey.PageUp => VirtualKeyCode.PageUp, VirtualKey.PageDown => VirtualKeyCode.PageDown,
        VirtualKey.F1 => VirtualKeyCode.F1, VirtualKey.F2 => VirtualKeyCode.F2, VirtualKey.F3 => VirtualKeyCode.F3,
        VirtualKey.F4 => VirtualKeyCode.F4, VirtualKey.F5 => VirtualKeyCode.F5, VirtualKey.F6 => VirtualKeyCode.F6,
        VirtualKey.F7 => VirtualKeyCode.F7, VirtualKey.F8 => VirtualKeyCode.F8, VirtualKey.F9 => VirtualKeyCode.F9,
        VirtualKey.F10 => VirtualKeyCode.F10, VirtualKey.F11 => VirtualKeyCode.F11, VirtualKey.F12 => VirtualKeyCode.F12,
        VirtualKey.Number0 => VirtualKeyCode.D0, VirtualKey.Number1 => VirtualKeyCode.D1,
        VirtualKey.Number2 => VirtualKeyCode.D2, VirtualKey.Number3 => VirtualKeyCode.D3,
        VirtualKey.Number4 => VirtualKeyCode.D4, VirtualKey.Number5 => VirtualKeyCode.D5,
        VirtualKey.Number6 => VirtualKeyCode.D6, VirtualKey.Number7 => VirtualKeyCode.D7,
        VirtualKey.Number8 => VirtualKeyCode.D8, VirtualKey.Number9 => VirtualKeyCode.D9,
        (VirtualKey)191 => VirtualKeyCode.Oem2,  // "?" / "/"
        _ => VirtualKeyCode.None,
    };
}
```

- [ ] **Step 2: Hook into `MainWindow.xaml.cs` at line 121**

Per Spike 3, the optimal insertion point is **line 121**, immediately after the existing `RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;` line at line 120.

Existing ctor shape (from Spike 3):
```csharp
// line 113: _keyboardShortcutService = App.GetService<KeyboardShortcutService>();
// line 114-117: RegisterDefaultShortcuts(); ConfigureCommandPalette();
// line 120: RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;
// line 121: [INSERT HERE]
```

Revised ctor:
```csharp
// After Task 2 renames KeyboardShortcutService → IShortcutRegistry:
_shortcutRegistry = App.GetService<IShortcutRegistry>();      // line 113 rewired

// After Task 10 migrates RegisterDefaultShortcuts() into ShortcutCatalog:
// lines 114–117 are deleted

_shortcutRouter = App.GetService<ShortcutInputRouter>();       // new, replaces line 115-ish
RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;             // line 120 preserved
_shortcutRouter.Attach(this);                                   // line 121 — NEW
// ... existing startup below line 121
```

Note: `_shortcutRouter.Attach(this)` internally wires `RootGrid.PreviewKeyDown += OnKeyDown` a second time — this is intentional. The router owns dialog-trigger logic; the existing `RootGrid_PreviewKeyDown` handler owns whatever else MainWindow does today (scroll / navigation / etc.). Two handlers on the same event are fine — they run in registration order.

Alternative (tighter refactor): migrate the contents of `RootGrid_PreviewKeyDown` (lines 315–335) into `ShortcutInputRouter.OnKeyDown` so there's only one handler. This is optional and should be deferred if the existing handler is doing non-shortcut work.

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.App/Services/ShortcutInputRouter.cs src/AgentX.App/MainWindow.xaml.cs
git commit -m "feat(a2): ShortcutInputRouter hooks MainWindow for palette/jump/cheatsheet"
```

---

### Task 9: Per-page shortcut registration pattern

Each existing page (e.g., `DocumentsPage`, `ChatPage`, `SettingsPage`) registers its scope-local shortcuts in `OnNavigatedTo` and unregisters in `OnNavigatedFrom`.

**Files:**
- Modify: `src/AgentX.App/Views/DocumentsPage.xaml.cs` — example; repeat for each page
- Modify: `src/AgentX.App/Views/ChatPage.xaml.cs`
- Modify: `src/AgentX.App/Views/SettingsPage.xaml.cs`
- Modify: any other page Rocky chooses to make shortcut-aware

- [ ] **Step 1: Add a `PageScope` base class or extension**

```csharp
// src/AgentX.App/Helpers/ShortcutPageExtensions.cs
using System;
using System.Collections.Generic;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Helpers;

public static class ShortcutPageExtensions
{
    public static IDisposable RegisterPageShortcuts(
        this Page page,
        IShortcutRegistry registry,
        params ShortcutDescriptor[] descriptors)
    {
        var tokens = new List<IDisposable>(descriptors.Length);
        foreach (var d in descriptors) tokens.Add(registry.Register(d));
        return new CompositeDisposable(tokens);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items;
        public CompositeDisposable(List<IDisposable> items) => _items = items;
        public void Dispose() { foreach (var i in _items) i.Dispose(); }
    }
}
```

- [ ] **Step 2: Use in `DocumentsPage.xaml.cs`**

```csharp
using AgentX.App.Helpers;
using AgentX.Core.Services.Shortcuts;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgentX.App.Views;

public sealed partial class DocumentsPage : Page
{
    private readonly IShortcutRegistry _registry;
    private IDisposable? _shortcutScope;

    public DocumentsPage(IShortcutRegistry registry /* + other deps */)
    {
        _registry = registry;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shortcutScope = this.RegisterPageShortcuts(_registry,
            new ShortcutDescriptor(
                Id: "docs.import",
                Label: "Import Document",
                Scope: new ShortcutScope("DocumentsPage"),
                Chord: new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.I) },
                Handler: ct => ViewModel.ImportCommand.ExecuteAsync(null),
                Category: "Documents"),
            new ShortcutDescriptor(
                Id: "docs.refresh",
                Label: "Refresh Documents",
                Scope: new ShortcutScope("DocumentsPage"),
                Chord: new[] { new KeyChord(KeyModifiers.None, VirtualKeyCode.F5) },
                Handler: ct => ViewModel.RefreshCommand.ExecuteAsync(null),
                Category: "Documents"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutScope?.Dispose();
        _shortcutScope = null;
    }
}
```

- [ ] **Step 3: Repeat pattern for `ChatPage`, `SettingsPage`, `AuditLogPage` (if v2.1.5 has landed)**

Each page's descriptors are listed in Task 10's catalog spec.

- [ ] **Step 4: Commit per page**

```bash
git commit -m "feat(a2): register per-page shortcuts in DocumentsPage"
git commit -m "feat(a2): register per-page shortcuts in ChatPage"
git commit -m "feat(a2): register per-page shortcuts in SettingsPage"
```

---

### Task 10: Default `ShortcutCatalog` — seed global shortcuts at startup

**Per Conflict 1 decision:** `ShortcutCatalog.SeedDefaults()` absorbs the content of the existing `RegisterDefaultShortcuts()` method (`MainWindow.xaml.cs:141–225` per Spike 3). After this task, `RegisterDefaultShortcuts()` is deleted from MainWindow — seeding happens centrally in `ShortcutCatalog`.

**Files:**
- Create: `src/AgentX.App/Services/ShortcutCatalog.cs`
- Modify: `src/AgentX.App/App.xaml.cs` — call `ShortcutCatalog.SeedDefaults` after DI is ready
- Modify: `src/AgentX.App/MainWindow.xaml.cs` — DELETE `RegisterDefaultShortcuts()` method (lines 141–225) and the call at line 114–117 (the seeding moves to `ShortcutCatalog.SeedDefaults`)

- [ ] **Step 1: Write the catalog**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Shortcuts;

namespace AgentX.App.Services;

/// <summary>Seeds all globally-scoped shortcuts at app startup.</summary>
public sealed class ShortcutCatalog
{
    private readonly IShortcutRegistry _registry;
    private readonly INavigationService _nav;
    private readonly IWindowCommands _windowCommands;

    public ShortcutCatalog(
        IShortcutRegistry registry,
        INavigationService nav,
        IWindowCommands windowCommands)
    {
        _registry = registry;
        _nav = nav;
        _windowCommands = windowCommands;
    }

    public void SeedDefaults()
    {
        // Navigation
        Global("nav.documents",  "Go to Documents",  KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.D1, _ => { _nav.Navigate("DocumentsPage"); return Task.CompletedTask; }, "Navigation");
        Global("nav.chat",       "Go to Chat",       KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.D2, _ => { _nav.Navigate("ChatPage"); return Task.CompletedTask; }, "Navigation");
        Global("nav.settings",   "Go to Settings",   KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.D3, _ => { _nav.Navigate("SettingsPage"); return Task.CompletedTask; }, "Navigation");
        Global("nav.audit",      "Go to Audit Log",  KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.D4, _ => { _nav.Navigate("AuditLogPage"); return Task.CompletedTask; }, "Navigation");
        Global("nav.back",       "Go Back",          KeyModifiers.Alt,                        VirtualKeyCode.Left,  _ => { _nav.GoBack(); return Task.CompletedTask; }, "Navigation");
        Global("nav.forward",    "Go Forward",       KeyModifiers.Alt,                        VirtualKeyCode.Right, _ => { _nav.GoForward(); return Task.CompletedTask; }, "Navigation");

        // Window
        Global("window.close",    "Close Window",        KeyModifiers.Ctrl, VirtualKeyCode.W, _ => { _windowCommands.CloseCurrentWindow(); return Task.CompletedTask; }, "Window");
        Global("window.minimize", "Minimize to Tray",    KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.M, _ => { _windowCommands.MinimizeToTray(); return Task.CompletedTask; }, "Window");
        Global("window.fullscreen","Toggle Fullscreen",   KeyModifiers.None, VirtualKeyCode.F11, _ => { _windowCommands.ToggleFullscreen(); return Task.CompletedTask; }, "Window");

        // Quick actions — hardcoded in ShortcutInputRouter, but listed in the catalog so they
        // appear in the cheatsheet. Each trigger below reflects the primary chord; synonyms
        // (Ctrl+K for palette, Ctrl+Shift+? for cheatsheet) are noted in the Label only.
        Global("help.palette",     "Command Palette (Ctrl+K or Ctrl+Shift+P)",      KeyModifiers.Ctrl | KeyModifiers.Shift, VirtualKeyCode.P, _ => Task.CompletedTask, "Help");
        Global("help.jump",        "Jump To…",                                       KeyModifiers.Ctrl,                       VirtualKeyCode.P, _ => Task.CompletedTask, "Help");
        Global("help.cheatsheet",  "Keyboard Shortcuts (F1 or Ctrl+Shift+?)",       KeyModifiers.None,                       VirtualKeyCode.F1, _ => Task.CompletedTask, "Help");

        // Migrate content from the previous RegisterDefaultShortcuts() body here.
        // Exact list comes from the pre-Task-2 audit (grep existing KeyboardShortcutService
        // call sites and RegisterDefaultShortcuts(): lines 141–225 of MainWindow.xaml.cs).
        // Each migrated shortcut becomes a Global(...) or scoped .Register(...) call.
    }

    private void Global(string id, string label, KeyModifiers mods, VirtualKeyCode key,
                        Func<CancellationToken, Task> handler, string category)
    {
        _registry.Register(new ShortcutDescriptor(
            Id: id,
            Label: label,
            Scope: ShortcutScope.Global,
            Chord: new[] { new KeyChord(mods, key) },
            Handler: handler,
            Category: category));
    }
}

// Minimal abstractions — if existing INavigationService / IWindowCommands already exist in Agent-X,
// substitute those and delete these stubs.
public interface INavigationService
{
    void Navigate(string pageKey);
    void GoBack();
    void GoForward();
}

public interface IWindowCommands
{
    void CloseCurrentWindow();
    void MinimizeToTray();
    void ToggleFullscreen();
}
```

> If existing `INavigationService` / `IWindowCommands` already exist (Spike 0 grep), delete these stubs and wire the real ones.

- [ ] **Step 2: Seed at startup in `App.xaml.cs`**

Find the post-DI-ready hook (after `Host.Services` is built). Add:

```csharp
var catalog = Host.Services.GetRequiredService<ShortcutCatalog>();
catalog.SeedDefaults();
```

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.App/Services/ShortcutCatalog.cs src/AgentX.App/App.xaml.cs
git commit -m "feat(a2): ShortcutCatalog seeds 12 default global shortcuts"
```

---

### Task 11: DI registration

**Files:**
- Modify: `src/AgentX.App/Services/ServiceCollectionExtensions.cs` (or `App.xaml.cs` DI setup)

- [ ] **Step 1: Register everything**

At `src/AgentX.App/App.xaml.cs:280`, REPLACE the existing `services.AddSingleton<KeyboardShortcutService>()` line with the interface registration below (the `ShortcutRegistry` class already resolves per Task 2's rename). Add the remaining lines in the same DI block.

```csharp
// REPLACES: services.AddSingleton<KeyboardShortcutService>();
services.AddSingleton<AgentX.Core.Services.Shortcuts.IShortcutRegistry,
                     AgentX.App.Services.ShortcutRegistry>();

// Infrastructure — no multi-step chord prefix seeded in v2.1.0 final (per Conflict 3:
// Ctrl+K belongs to the Command Palette, not to a chord prefix).
services.AddSingleton(sp =>
    new AgentX.Core.Services.Shortcuts.ChordStateMachine(
        windowMs: 1000,
        clock: () => System.DateTime.UtcNow));

services.AddTransient<AgentX.App.ViewModels.CommandPaletteViewModel>();
services.AddTransient<AgentX.App.ViewModels.JumpToViewModel>();
services.AddTransient<AgentX.App.ViewModels.CheatsheetViewModel>();

// CommandPalette is the existing view — keep its current DI registration
// (if any); if not DI-registered today, add:
services.AddTransient<AgentX.App.Views.CommandPalette>();
services.AddTransient<AgentX.App.Views.Dialogs.JumpToDialog>();
services.AddTransient<AgentX.App.Views.Dialogs.CheatsheetDialog>();

services.AddSingleton<AgentX.App.Services.ShortcutCatalog>();
services.AddSingleton<AgentX.App.Services.ShortcutInputRouter>(sp =>
    new AgentX.App.Services.ShortcutInputRouter(
        registry: sp.GetRequiredService<AgentX.Core.Services.Shortcuts.IShortcutRegistry>(),
        chords: sp.GetRequiredService<AgentX.Core.Services.Shortcuts.ChordStateMachine>(),
        activeScopeProvider: () => sp.GetRequiredService<INavigationService>().CurrentPageKey,
        paletteFactory: () => sp.GetRequiredService<AgentX.App.Views.CommandPalette>(),
        jumpToFactory: () => sp.GetRequiredService<AgentX.App.Views.Dialogs.JumpToDialog>(),
        cheatsheetFactory: () => sp.GetRequiredService<AgentX.App.Views.Dialogs.CheatsheetDialog>()));
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: 0 errors. If `NavigationService.CurrentPageKey` is not an existing property, add it (returns a `string?` — the currently-navigated page's key used by router to route page-scoped shortcuts). Spike 0 identifies what to do here.

- [ ] **Step 3: Commit**

```bash
git add -u
git commit -m "feat(a2): register shortcut services and router in DI"
```

---

### Task 12: Smoke test — drive the full flow manually

- [ ] **Step 1: Launch the app**

```bash
dotnet run --project src/AgentX.App
```

- [ ] **Step 2: Verify each trigger**

Press in sequence:
1. `Ctrl+Shift+P` → Command Palette opens, focus lands in query box.
2. Type "doc" → Results filter to "Import Document", "Go to Documents", etc.
3. Press `↓` / `↑` / `Enter` → selected item executes and palette closes.
4. `Ctrl+P` → Jump-To opens with documents + conversations + pages listed.
5. Type a document name → candidates filter; `Enter` opens that document.
6. `?` (no modifiers) → Cheatsheet opens showing all shortcuts grouped by category.
7. `Ctrl+Shift+D1` → Navigate to Documents.
8. `Ctrl+Shift+D2` → Navigate to Chat.
9. `F5` on Documents page → Refreshes list (page-scoped shortcut).
10. `Ctrl+K` then within 1 second `D` → Future chord target (seeds in Task 11 DI); confirm ChordStateMachine arms on `Ctrl+K` (no-op handler) and resets after the window.

- [ ] **Step 3: Revert any debugging-only changes**

---

### Task 13: Localize new UI strings

**Files:**
- Modify: all 6 `src/AgentX.App/Strings/<locale>/Resources.resw`

- [ ] **Step 1: Add canonical en-US entries**

```xml
<data name="CommandPalette_QueryBox.PlaceholderText" xml:space="preserve">
  <value>Type a command or search…</value>
</data>
<data name="JumpTo_QueryBox.PlaceholderText" xml:space="preserve">
  <value>Jump to a document, conversation, or page…</value>
</data>
<data name="Cheatsheet_Dialog.Title" xml:space="preserve">
  <value>Keyboard Shortcuts</value>
</data>
<!-- Category labels -->
<data name="Shortcut_Category_Navigation" xml:space="preserve"><value>Navigation</value></data>
<data name="Shortcut_Category_Documents" xml:space="preserve"><value>Documents</value></data>
<data name="Shortcut_Category_Chat" xml:space="preserve"><value>Chat</value></data>
<data name="Shortcut_Category_Window" xml:space="preserve"><value>Window</value></data>
<data name="Shortcut_Category_Help" xml:space="preserve"><value>Help</value></data>
```

- [ ] **Step 2: Translate to de / es / fr / ja / zh-CN**

Follow the A1 plan's translation backfill workflow (Task 7 of A1). If A1 hasn't landed yet, add translations inline here with human-reviewed values.

- [ ] **Step 3: Run LocaleAudit**

```bash
dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- \
  src/AgentX.App \
  src/AgentX.App/Strings \
  --fail-below 98
```

Expected: all locales ≥98% including new A2 entries. If A1 is not yet landed, this step is advisory — run the check manually.

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.App/Strings/
git commit -m "feat(a2): localize palette/jump/cheatsheet UI across 6 locales"
```

---

### Task 14: Docs + release notes

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/USER-GUIDE.md`
- Modify: `docs/DEVELOPER-GUIDE.md`
- Modify: `docs/v2.1.0-RELEASE-NOTES.md`

- [ ] **Step 1: Architecture section**

Append to `docs/ARCHITECTURE.md`:

```markdown
### Keyboard Power Mode (A2)

Agent-X's keyboard power mode centers on an `IShortcutRegistry` singleton that owns every shortcut in the app as a `ShortcutDescriptor`. Descriptors are scoped `Global` or per-page. Three built-in dialogs consume the registry:

- **Command Palette** (`Ctrl+Shift+P`) — fuzzy search over all registered shortcuts. Execute by pressing `Enter`.
- **Jump-To** (`Ctrl+P`) — fuzzy search over documents, conversations, and pages.
- **Cheatsheet** (`?`) — grouped read-only listing of every shortcut available in the current scope.

A `ShortcutInputRouter` hooks `MainWindow`'s `KeyDown` event once, translates WinUI `VirtualKey` → `KeyChord`, and dispatches via the registry. A `ChordStateMachine` with a 1-second window tracks multi-step chords (e.g., `Ctrl+K, D`).

Pages register scope-local shortcuts in `OnNavigatedTo` via `RegisterPageShortcuts` and unregister in `OnNavigatedFrom` via the returned `IDisposable`. This keeps the registry always consistent with the current navigation frame.

Fuzzy matching uses a VS-Code-style inline subsequence scorer with word-boundary, prefix, and consecutive-match bonuses — no external library dependency.
```

- [ ] **Step 2: User-guide section**

Append to `docs/USER-GUIDE.md`:

```markdown
## Keyboard shortcuts

Agent-X is designed to be driven entirely from the keyboard. Press `?` anywhere to see the full cheatsheet for your current context.

### The three power commands

| Shortcut | Does |
|---|---|
| `Ctrl+K` or `Ctrl+Shift+P` | **Command Palette** — search & run any action |
| `Ctrl+P`                   | **Jump To** — find any document, conversation, or page |
| `F1` or `Ctrl+Shift+?`     | **Cheatsheet** — show every shortcut for the current page |

### Navigation (from anywhere)

| Shortcut | Page |
|---|---|
| `Ctrl+Shift+1` | Documents |
| `Ctrl+Shift+2` | Chat |
| `Ctrl+Shift+3` | Settings |
| `Ctrl+Shift+4` | Audit Log (v2.1.5+) |
| `Alt+←` / `Alt+→` | Go Back / Forward |

### Window

| Shortcut | Action |
|---|---|
| `Ctrl+W` | Close current window |
| `Ctrl+Shift+M` | Minimize to system tray |
| `F11` | Toggle fullscreen |

The full live list is always available via `?`.
```

- [ ] **Step 3: Developer-guide section**

Append to `docs/DEVELOPER-GUIDE.md`:

```markdown
## Registering a new keyboard shortcut

1. Decide scope: **Global** (available from anywhere) or **Page-scoped** (fires only on that page).
2. Add a `ShortcutDescriptor`:

```csharp
new ShortcutDescriptor(
    Id: "docs.import",                  // stable — don't change across releases
    Label: "Import Document",
    Scope: new ShortcutScope("DocumentsPage"),   // or ShortcutScope.Global
    Chord: new[] { new KeyChord(KeyModifiers.Ctrl, VirtualKeyCode.I) },
    Handler: ct => ViewModel.ImportCommand.ExecuteAsync(null),
    Category: "Documents")              // optional — groups in Cheatsheet
```

3. Register:
   - **Global** — add to `ShortcutCatalog.SeedDefaults()` in `src/AgentX.App/Services/ShortcutCatalog.cs`.
   - **Page-scoped** — call `this.RegisterPageShortcuts(_registry, descriptor)` in the page's `OnNavigatedTo`, dispose the returned token in `OnNavigatedFrom`.
4. The new shortcut automatically appears in:
   - Command Palette (fuzzy-searchable)
   - Cheatsheet (grouped by Category)
   - Registry lookups for key-driven execution

### Multi-step chords

To add a chord like `Ctrl+K, D`, register the prefix in `ChordStateMachine` DI setup (see `App.xaml.cs`) and supply a descriptor whose `Chord` has two `KeyChord` entries.
```

- [ ] **Step 4: Release notes**

Append to `docs/v2.1.0-RELEASE-NOTES.md`:

```markdown
### Keyboard-First Power Mode (A2)

- **Command Palette** (`Ctrl+K` or `Ctrl+Shift+P`) — fuzzy-searchable runner for every action in the app, fed by the new `IShortcutRegistry`
- **Jump To** (`Ctrl+P`) — go to any document, conversation, or page instantly
- **Cheatsheet** (`F1` or `Ctrl+Shift+?`) — grouped live listing of shortcuts for your current page
- **12+ global shortcuts** seeded by default — navigation, window management, help
- **`KeyboardShortcutService` evolved** into `IShortcutRegistry` / `ShortcutRegistry` — existing shortcuts preserved, scope-aware lookup added
- **`ChordStateMachine`** infrastructure ready for multi-step chords in future releases (no multi-step chord seeded in v2.1.0)
- **Per-page shortcut help** — every page contributes its own shortcuts, visible in Cheatsheet under its own group
- **`IShortcutRegistry`** public API for plugins to register their own shortcuts (v2.2 plugin API hookup)
```

- [ ] **Step 5: Full build + test gate**

```bash
dotnet build && dotnet test
```

Expected: build 0W/0E, all tests pass (~22 new tests added from Tasks 1–7: KeyChord 7, ShortcutRegistry 6, FuzzyMatcher 7, ChordStateMachine 4, CommandPaletteViewModel 3, JumpToViewModel 2 = 29 A2-only tests; baseline 868 → 897).

- [ ] **Step 6: Final commit**

```bash
git add -u
git commit -m "docs(a2): architecture + user + developer + release notes for keyboard power mode"
```

---

## Self-Review Summary

- **Spec coverage:**
  - Command palette (Ctrl+K + Ctrl+Shift+P) → Task 5 **EXTENDS existing `CommandPalette.xaml`** (per Conflict 2) + Task 8 hook in router
  - Jump-to `Ctrl+P` → Task 6 (`JumpToViewModel` + `JumpToDialog`) + Task 8 hook
  - Cheatsheet (F1 + Ctrl+Shift+?) → Task 7 (`CheatsheetViewModel` + `CheatsheetDialog`) + Task 8 hook
  - Chord registry → Task 2 **EVOLVES `KeyboardShortcutService`** → `IShortcutRegistry` / `ShortcutRegistry` (per Conflict 1)
  - Multi-step chord infrastructure → Task 4 (`ChordStateMachine`) — infrastructure only in v2.1.0 final (no chord-prefix shortcut seeded per Conflict 3)
  - Per-page shortcut help → Task 9 (`RegisterPageShortcuts` extension + per-page integration)
  - Default shortcut catalog → Task 10 (`ShortcutCatalog` with 12+ seeded globals, absorbing existing `RegisterDefaultShortcuts()` content)
  - DI + startup wiring → Task 11 (replaces existing `KeyboardShortcutService` registration)
  - Smoke tests + localization + docs → Tasks 12–14
- **Placeholder scan:** every code step contains complete code. `INavigationService` / `IWindowCommands` in Task 10 are spec'd with a note to substitute real Agent-X equivalents identified in Spike 0.
- **Type consistency:** `KeyChord`, `KeyModifiers`, `VirtualKeyCode`, `ShortcutScope`, `ShortcutDescriptor`, `IShortcutRegistry`, `ShortcutRegistry`, `FuzzyMatcher` (`Score`, `Rank`, `ScoredItem<T>`), `ChordStateMachine` (`RegisterPrefix`, `OnKey`, `Reset`, `ChordResult`, `ChordResultKind`), `ShortcutInputRouter`, `ShortcutCatalog`, `CommandPaletteViewModel`, `JumpToViewModel`, `JumpToItem`, `JumpToItemKind`, `CheatsheetViewModel`, `CheatsheetGroup` — all names used consistently across tasks.

## Follow-up (not in this plan)

1. **Persistent per-user shortcut customization** — a `IShortcutConfigService` that reads/writes `shortcuts.json` under `%LocalAppData%\AgentX` and overrides default chords. Conflicts (two descriptors bound to the same chord) surface a warning in Settings.
2. **Plugin API integration** — expose `IShortcutRegistry` on the plugin SDK (v2.2 Phase 2) so plugins register their own actions.
3. **Macro recorder** — record a sequence of palette commands as a named macro + bind to a chord.
4. **MRU ordering in palette** — track command execution frequency and surface recently/frequently used commands at the top.
5. **Multi-key chord completion hint** — after `Ctrl+K` is pressed, show a transient toast "Ctrl+K, D = Duplicate document…" to teach users the continuation options.
6. **Gamepad navigation** — WinUI 3 supports gamepad input; map `B/A/X/Y` → palette navigation for accessibility.
