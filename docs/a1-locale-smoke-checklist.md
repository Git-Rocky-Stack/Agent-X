# A1 — End-to-End Locale Smoke Checklist

> **Scope:** Manual, visual QA for each of the six shipping locales. The
> `LocaleAudit` CI gate plus `PerPageLocaleSnapshotTests` already guarantee
> data-layer correctness (every referenced key resolves in every locale,
> zero orphans, ≥98% coverage, key-set parity). This checklist covers the
> **visual** surface that tooling cannot see: text overflow, glyph fallback,
> FlowDirection, and culture-sensitive formatting.

## When to run

- After any edit that touches `src/AgentX.App/**/*.xaml`, a resw bundle, or
  `LocalizationService`.
- Before every v2.x release that ships to the Store.
- Required sign-off for v2.1.0 final.

## Prerequisites

- Windows 11 workstation with CJK font support (`Yu Gothic`, `Microsoft YaHei`)
  and Arabic/Hebrew font support (`Segoe UI`, `Tahoma`) present — standard on
  the default Windows 11 SKU.
- Debug or Release build of `AgentX.App` for the target platform.
- A scratch branch — the culture override step touches `App.xaml.cs` and
  **must not be committed**.

## Procedure — per locale (de / es / fr / ja / zh-CN / en-US)

### 1. Override the UI culture

In `src/AgentX.App/App.xaml.cs`, inside the application constructor (before
any window is shown), **temporarily** set:

```csharp
// SMOKE ONLY — REVERT BEFORE COMMIT
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("<locale>");
```

Replace `<locale>` with the one under test (`de`, `es`, `fr`, `ja`, `zh-CN`,
`en-US`).

### 2. Launch

```powershell
dotnet run --project src/AgentX.App
```

### 3. Walk every page

Navigate through the full nav menu (every `NavigationViewItem` in
`MainWindow.xaml`). At each page, verify the following checkboxes.

#### ✅ Checklist — repeat per page, per locale

- [ ] **No raw `x:Uid` leakage.** No button / label / tooltip renders the
      literal resource key (e.g., `Plugin_Manager`, `Encryption_Toggle.Text`).
      Any leak = a missing / misnamed resw entry.
- [ ] **No text overflow or clipping.** German and French strings tend to run
      1.3× longer than English — verify nav-pane labels, button content, and
      status-bar captions are not truncated.
- [ ] **No garbled glyphs (CJK locales).** Japanese / Simplified Chinese text
      must render cleanly. Tofu boxes (□) or mojibake (錆 → 閹) indicate a
      missing font fallback — report immediately.
- [ ] **FlowDirection is left-to-right.** All six current locales are LTR;
      the nav pane must remain on the left, content mirrored correctly.
- [ ] **Number / date formatting respects culture.** Status bar document
      counts, Workflow run timestamps, and indexing progress values must
      format with the locale's conventions (e.g., German `1.234` vs.
      English `1,234`).
- [ ] **Format placeholders render.** Strings containing `{0}` / `{1}` (e.g.,
      `"Imported {0} documents"`) must not leak the placeholder — if they do,
      the call site is passing the args in the wrong order.

### 4. Capture evidence (optional, recommended)

Save a screenshot of the Dashboard, Chat, Plugin Manager, and Settings
pages per locale to `docs/images/a1-locale-smoke/<locale>/` (create the
folder if missing). These become release-notes supporting material for
stakeholders.

### 5. Revert the culture override

```bash
git checkout src/AgentX.App/App.xaml.cs
```

Verify `git diff --stat` reports no remaining changes in `App.xaml.cs`
before continuing.

## RTL readiness — ar-SA pseudo-locale (one-time)

FlowDirection must flip to RightToLeft even though Agent-X ships no Arabic
resw today. This is a **visual-only** check; raw `x:Uid` text is expected.

1. Apply the culture override with `"ar-SA"` per step 1 above.
2. Launch the app.
3. Verify: **nav pane on the RIGHT**, content area mirrored, nav item icons
   flipped horizontally where appropriate. All text will render as raw
   resource keys — that is expected and correct (no Arabic bundle yet).
4. Capture a screenshot as `docs/images/a1-locale-smoke/ar-SA-rtl-readiness.png`.
5. **Revert** the culture override.

## Pass criteria

- All six shipping locales complete every checkbox with no ❌ findings.
- ar-SA pseudo-locale verified RTL-flipped.
- No commits contain the culture override line.
- Screenshots uploaded (recommended, not required).

Any ❌ finding blocks the release and feeds back to resw repair or XAML fix.

## Failure triage quick-reference

| Symptom | Likely root cause | Fix |
|---|---|---|
| Literal `x:Uid` text on screen | Missing resw entry or typo in key | Add/correct entry in the affected locale's resw and re-run `LocaleAudit` |
| Text clipped in nav pane | Overflow in longer language | Increase `OpenPaneLength` or use `TextTrimming="CharacterEllipsis"` with a tooltip |
| Tofu / mojibake in CJK | Font fallback missing | Verify `FontFamily="Segoe UI"` (falls back to Yu Gothic / YaHei) is present |
| FlowDirection did not flip for ar-SA | `FlowDirectionHelper` not called | Confirm `MainWindow.xaml.cs` sets `RootGrid.FlowDirection` after `InitializeComponent()` |
| `{0}` placeholder visible | Missing format args | Audit the `GetString(key, args...)` call; pass the expected count |
