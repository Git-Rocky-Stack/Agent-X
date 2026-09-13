# Changelog

All notable changes to Agent-X are documented in this file.

---

## Documentation Release (2026-05-03)

### Added — Comprehensive User Documentation Suite (20+ files, 8,000+ lines)

**Getting Started & Onboarding**
- `docs/user-guide/getting-started/quick-start.md` — 10-minute setup walkthrough for new users
- Covers installation, first launch, passphrase creation, document import, AI chat, and semantic search

**Reference Documentation**
- `docs/user-guide/faq.md` — 100+ frequently asked questions covering installation, features, AI, search, performance, privacy, licensing, and troubleshooting
- `docs/user-guide/troubleshooting.md` — Solutions to common issues organized by category with advanced diagnostics section
- `docs/user-guide/glossary.md` — 100+ term glossary with definitions covering all Agent-X terminology
- `docs/user-guide/keyboard-shortcuts.md` — Comprehensive power user navigation guide with platform-specific notes

**Templates & Scenarios**
- `docs/user-guide/templates/README.md` — Templates overview with usage guide
- `docs/user-guide/templates/document-templates.md` — Project Brief, Meeting Notes, Research Summary, Technical Spec, Code Review templates
- `docs/user-guide/templates/chat-templates.md` — Summarize, Compare, Extract, Research, Technical chat templates
- `docs/user-guide/scenarios/README.md` — Real-world scenarios: Research Paper Analysis, Meeting Intelligence, Code Review Assistant, Document Migration, Personal Knowledge Base

**Video Tutorial Scripts**
- `docs/user-guide/video-scripts/README.md` — Scripts for 5 videos: Quick Start, Advanced RAG, Knowledge Graph, Workflows, GPU Acceleration

**AI Discovery & Indexing**
- `docs/llms.txt` — AI-optimized documentation index for LLM consumption with quick links and key concepts
- `docs/long-llms.txt` — Extended AI reference with comprehensive details on architecture, features, configuration, and best practices

### Updated
- **README.md** — Added comprehensive documentation section with links to all new user guide resources
- Documentation organized under `docs/user-guide/` with clear categorization

### Documentation Statistics
- **Total Files Created:** 20+
- **Total Lines:** 8,000+
- **Categories:** 7 (Getting Started, Reference, Templates, Scenarios, Video Scripts, AI Discovery, Enhanced)
- **Topics Covered:** 100+ glossary terms, 100+ FAQ items, 5 real-world scenarios, 10+ templates, 5 video scripts

---

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed - The adopt hole behind Ctrl+N, closed at its cause (2026-09-12)

The entry below stopped the generation Ctrl+N left behind and claimed the abandoned thread
therefore "cannot arrive late". **That claim was false**, and two more holes sat beside it.
Cancelling is not a guarantee, because the code being cancelled decides when to look:

- **A cancelled stream can still complete, and still be adopted.** Cancellation is
  cooperative. A stream that has already left the token loop and reached the raise at
  `MessagingCoordinator.cs:198` fires `StreamingCompleted` whether or not the cancel has
  landed; only the `OperationCanceledException` arm at `:212` returns silently. So the adopt
  branch still saw a null `ActiveConversationId`, took the finished stream's id back, and filed
  a sidebar row for it. Proven by
  `NewConversationAsync_MidGeneration_DiscardsAStreamThatCompletesDespiteTheCancel`
  (`tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs:807`), which failed with `found 42L`
  before the fix.
- **A cleanly cancelled stream re-stamped its context story on the blank conversation, with no
  race at all.** The cancel arm back-fills `ContextInspection` from the conversation it was
  generating for (`MessagingCoordinator.cs:219`), and the send continuation applied it with no
  check on which thread was now on screen, just after `NewConversationAsync` had cleared it.
  Deterministic, not a race. Proven by
  `NewConversationAsync_MidGeneration_DiscardsTheCancelledStreamsContextInspection`
  (`ChatViewModelTests.cs:861`).
- **Ctrl+N was not the only way in.** Picking another thread in the sidebar moves the screen
  off a running generation too, and `SelectConversationAsync` cancelled nothing, so the adopt
  branch dragged the screen back to the generating thread and filed a duplicate row for a
  conversation the sidebar already listed. Proven by
  `SelectConversationAsync_MidGeneration_DiscardsTheStreamItLeavesBehind`
  (`ChatViewModelTests.cs:896`), which failed with `found 84L`.

All three are one defect: `OnStreamingCompleted` adopted a conversation id without asking
whether that conversation was still on screen. A conversation epoch answers it
(`src/AgentX.App/ViewModels/ChatViewModel.cs:336`). Leaving a thread advances the epoch
(`ChatViewModel.cs:828` for Ctrl+N, `:866` for picking another thread); a generation carries
the epoch it started under (`:762`); and `GenerationStillOwnsScreen` (`:498`) gates both the
completion handler (`:404`) and the send continuation (`:782`). None of it depends on
cancellation timing, so the window is closed rather than narrowed.

`StreamingCompletedEvent_AppliesContextInspectionSnapshot` (`ChatViewModelTests.cs:136`) was
not weakened to make room. It raises completion with no generation of the view model's own in
flight; the epoch is null there, and adoption still happens by design, which is what a
completion from another surface sharing the coordinator needs. Each of the three parts of the
fix was removed in turn and the suite re-run: the handler gate alone accounts for two
failures, the continuation gate and the sidebar epoch for one each.

### Fixed - Guards that could not fail, could not see, or could not run (2026-09-12)

- `NavRailParityTests.EveryRailItem_SitsUnderAGroupPlacard` and
  `NoRailGroup_IsHeaderless_BetweenSeparators` walked the rail's direct children and asserted
  only that the offender list was empty. Pointed at an empty element, both reported no
  offenders and passed: verified by stubbing `MenuItemsElement()` to return an empty rail,
  against which all five rail guards passed. They now assert that the walk reached the rail and
  that its direct children account for every `NavigationViewItem` under `MenuItems`
  (`tests/AgentX.Tests/CodeQuality/NavRailParityTests.cs:184`), so nesting an item one level
  down fails loudly instead of quietly going unchecked. Verified both ways: with an item
  nested, the pre-fix guards passed 5/5 and the fixed ones report `saw 28 of the 29 rail
  items`.
- `WidthCappedColumnsDeclareAlignmentTests` read `Views` at the top level only, leaving
  `Views/Dialogs`, `Controls` and `MainWindow.xaml` unguarded. It now reads every XAML file
  under `AgentX.App` (`WidthCappedColumnsDeclareAlignmentTests.cs:80`). Verified by planting a
  `<Grid MaxWidth="900">` with no alignment in `Views/Dialogs/JumpToDialog.xaml`: the old guard
  passed, the widened one names the file and line. `Styles` and `Themes` hold no `MaxWidth` at
  all, so widening cost nothing.
- `SpacingIsOnTheFourPixelGridTests` split thickness components on commas only, so
  `Margin="4 6 4 6"` parsed as a single unreadable component and was skipped. XAML accepts both
  forms. It now splits on whitespace too (`SpacingIsOnTheFourPixelGridTests.cs:83`). Verified by
  planting that exact margin: the old guard passed, the fixed one catches it. No
  space-separated spacing exists in the app today, so nothing needed snapping.
- The four `VerifySweepSuppressionsTests` resolved the repo root by looking for a `.git`
  *directory* (`tests/AgentX.Tests/CodeQuality/VerifySweepSuppressionsTests.cs:228`). In a git
  worktree `.git` is a file holding `gitdir: ...`, so all four threw
  `DirectoryNotFoundException` there. A worktree is exactly where a clean-tree build gets
  checked, so the one place these guards most needed to run was the one place they could not.
  Found by building this commit in a worktree: 4 failures there, 0 in the main clone. It now
  accepts either shape, and the pristine worktree runs 3,029 of 3,029.

### Added - Runtime centering and Ctrl+N checks in the nav smoke (2026-09-12)

`scripts/uia-nav-smoke.ps1` proved per-page navigation and the palette, but never measured the
centering it was written alongside, and never pressed Ctrl+N.

- **Content centering** (`scripts/uia-nav-smoke.ps1:393`): every page whose named,
  not-Collapsed `ScrollViewer` wraps a column capped at 600 or more is visited, and the union
  of that scroller's onscreen descendants is measured against it. Weekly Digest, Model Manager
  and Knowledge Vault all measure `gutters 122/122` or `262/262`, `skew 0`, and `width 1136` of
  a 1200 cap, in both shifts. 1136 is the cap less `PaddingPage`'s 64
  (`src/AgentX.App/Styles/Colors.xaml:569`).

  Both failure modes are asserted, because each hides the other. Removing
  `HorizontalAlignment="Center"` from Weekly Digest's column changed nothing measurable: the
  `ContentColumn.Fill` MinWidth binding already pins the column to the cap, and a cap-width
  column centers correctly either way. Removing the binding reproduced the original report
  exactly, at `gutters 373/41, skew 332` with width down to 1095. So the check asserts gutter
  symmetry *and* that the column still fills the cap.

  The page set is read from the XAML's structure rather than from the `ContentColumn.Fill`
  binding under test: an earlier draft keyed off the binding, and removing the fix then dropped
  Weekly Digest from the scan instead of failing it.
- **Ctrl+N** (`:451`): from a page that is not Chat, the accelerator must select Chat on the
  rail and leave a blank conversation on screen. Verified by deleting the registration at
  `src/AgentX.App/Services/ShortcutCatalog.cs:76`, against which the check reports
  `Chat selected False -> False`.

70 checks across both shifts, 0 failed.

### Fixed - Review round on the parity sweep (2026-09-12)

Cloud review of the sweep, findings verified against the code before acting on them.

- **Ctrl+N mid-generation took the operator back to the thread they had just left**
  (`src/AgentX.App/ViewModels/ChatViewModel.cs:762`). The sweep widened "New Conversation"
  to a global chord and a palette action, so a generation started in the previous thread can
  still be running when the blank one opens. `NewConversationAsync` cleared the view state
  but left that stream running; when it finished, `OnStreamingCompleted`'s adopt branch saw
  the null id, took the finished stream's conversation id back and filed a second sidebar row
  for it, so the new conversation silently became the old one. It now stops the generation and
  clears the streaming state first. Covered by
  `NewConversationAsync_MidGeneration_CancelsTheStreamItLeavesBehind`. Reachable before the
  sweep from the Chat page's own button and Ctrl+Shift+N; the sweep widened who could hit it.
  Stopping the generation was necessary but not sufficient; the entry above corrects the
  claim this one originally made about it.
- `case "RefreshDashboard"` in `AppNavigationService.ExecuteAction` lost its only producer when
  the palette row was dropped (`ExecuteAction` is reached only from
  `CommandPalette.xaml.cs:656`). Removed, and `docs/ARCHITECTURE.md` no longer lists it as a
  palette action.
- `LampTile.OnCompactChanged` assigned `Cap.Padding` from a ternary whose two branches became
  identical when the non-compact side moved from 10 to 8. Removed: `LampTile.xaml:32` already
  sets `Padding="8,0"`, and IsCompact really only changes Height and FontSize.
- The palette's `Radius(token)` helper (`src/AgentX.App/Controls/CommandPalette.xaml.cs:408`)
  fell back to `CornerRadius(0)` when a token did not resolve. Zero is itself a stop on the
  machined scale, so a renamed or mistyped key rendered the wrong shape with nothing to catch
  it. It throws now, the way a missing StaticResource does in XAML.
- `scripts/apply-guide-accuracy.py` documented a `_blockComment` changeset key and defined
  `DEFAULT_BLOCK_COMMENT`, but the append path still wrote the hardcoded 2026-07 comment, so
  any future changeset would have stamped the wrong provenance above its keys in all six
  locales. The key is read now.

### Changed - Design parity sweep: rail, palette, orphan primitives, spacing grid (2026-09-06)

The 2026-09-06 parity sweep left a NOT DONE list. This closes it, and closes what the
audit behind it found once it was widened from "styles Hardware.xaml declares" to "every
keyed resource in the style dictionaries".

**Navigation rail** (`src/AgentX.App/MainWindow.xaml`)
- Weekly Digest and Analytics shared the area-chart glyph; Backup and Restore wore the
  Knowledge Vault's library. Digest now wears CalendarWeek (E8C0), Backup wears
  SaveLocal (E78C). Both verified present in Segoe Fluent Icons.
- Annotations sat between two separators with no group placard. It is a highlight on a
  vault document, so it now lives under KNOWLEDGE after Compare Documents.
- Past Self was on the rail but missing from the shell's nav-item map
  (`MainWindow.xaml.cs` BuildNavItemMap), so arriving there from the palette or Jump To
  never highlighted the rail item. Found by the new guard, fixed.

**Ctrl+K palette** (`src/AgentX.App/Controls/CommandPalette.xaml.cs`,
`MainWindow.xaml.cs` ConfigureCommandPalette)
- The palette carried a hardcoded English list of nine pages; the rail has twenty-nine.
  The shell now registers every rail item with the palette at startup: same tag,
  localized label, glyph and group placard, in rail order, with Settings under SYSTEM.
  A page added to the rail is in the palette.
- Shortcut hints come from the live registry (`ShortcutCatalog.PageChordDisplay`), not
  literals.
- `CommandPaletteViewModel` was registered in DI and unit-tested but never used by the
  control (its own comment deferred the integration to a task that never ran). It now
  supplies and executes the "On This Page" group: the shortcuts the current page
  registered. Its Core `FuzzyMatcher` is the palette's ranking engine, so the guide's
  fuzzy-match claim is true.
- Ranking and rendering used different orders, so Enter could run a row other than the
  highlighted one. Rows are now rendered in the order they are ranked, grouped stably.
- "New Conversation" and "Import Files" were plain navigations wearing action names.
  They now carry a navigation intent (`Services/NavigationIntents.cs`): Chat starts a
  fresh thread (`ChatViewModel.ApplyNavigationParameterAsync`), the vault raises the
  import picker (`KnowledgeVaultPage.OnNavigatedTo`). Global Ctrl+N carries the same
  intent. "Refresh Dashboard" was dropped: the dashboard re-initializes on every arrival,
  so it duplicated the Dashboard row under a different name.
- Every palette string is localized in all six locales (10 new keys,
  `scripts/translations/palette-l10n-2026-09.json`), and the four user-guide claims about
  the palette that the code did not support (prefix syntax, Tab-to-peek, recency ranking,
  documents and plugins in the palette, a fuzzy example that could not match) are rewritten
  in all six locales. `scripts/apply-guide-accuracy.py` now takes a changeset path.

**Orphan primitives and legacy tokens** (`src/AgentX.App/Styles/*.xaml`)
- Audit: 34 styles and roughly 200 tokens with no consumer anywhere in the app. Wired
  where a hand-rolled twin was waiting: `ControlCapStyle` under the five card styles that
  restated it (`Controls.xaml`, `Documents.xaml`); `ConversationItemStyle` on the chat
  list and dashboard recents; `StreamStyle` on the code-block fallback text
  (`MarkdownMessageControl.xaml.cs`); `ChromeCapButtonStyle` on the onboarding Launch
  button, the one polished CTA DESIGN.md allows per view.
- Deleted the rest: the Tier 1 Border primitives that duplicated the `Faceplate` and
  `LampTile` templates (`Hardware.xaml`), status badges, dead chat and document styles,
  five typography styles, the legacy `Spacing*`, `Radius*`, `Padding*`, `Border*`,
  `Shadow*`, `Transition*` tokens and the unreferenced colour ramps in `Colors.xaml`.
  Every `Radius*` consumer was re-pointed at the stop it already resolved to.
- `ArmedCapPressedBrush` had no consumer because the cap template never swapped fills on
  press. The machined cap template now darkens the cap 14% on press alongside the 1px
  travel, which is the `ArmedDeep` pressed state on the armed cap and works on every fill.
- DESIGN.md's recipe section pointed at the deleted styles and at a `HexBolt` control
  that never existed (the bolts are inline in the Faceplate template, inset 8px, not 10).
  Corrected, with the decisions logged.

**Plugin Manager** (`src/AgentX.App/Views/PluginManagerPage.xaml`)
- The ACTIVE/DISABLED pill was hand-rolled in code-behind with Tailwind green and red and
  hardcoded English. It is a `LampTile` now: GO while enabled, STBY unlit while parked.

**Annotation ink** (`src/AgentX.App/Helpers/AnnotationInk.cs`)
- The five persisted inks moved out of the Annotations page into their own file, which is
  now the only file the palette hue guard exempts. The guard also checks that file holds
  exactly the six ink literals, so chassis colour cannot hide behind the exemption.

**Spacing grid** (`scripts/normalize-spacing.py`)
- 884 spacing components across 46 files sat between DESIGN.md's base-4 stops (6, 10, 14
  and a few others). Snapped to the nearest multiple of 4, ties toward compact; components
  0 to 4 and negatives are left as optical adjustments. The rule is written into DESIGN.md.
- One code-behind radius of 6 (`BranchCompareWindow.xaml.cs`) moved to the cap stop.

**Page centering** (`src/AgentX.App/Views/*.xaml`)
- Rocky's report during review: Weekly Digest and Model Manager rendered too far to the
  right. Both wrap content in a 1200px MaxWidth grid inside a ScrollViewer, as Dashboard
  does, but Dashboard's grid declares `HorizontalAlignment="Center"` and theirs did not.
  Sixteen width-capped columns across twelve pages had no alignment; UI Automation captures
  measured six of them 230 to 540px right of center (Backup and Restore, Weekly Digest,
  Knowledge Vault, Model Manager, Semantic Search, Collaborative Sync), Model Manager running
  off the window. All sixteen now declare Center. Pre-existing: the same six pages measure
  the same skew on the pre-sweep build. `WidthCappedColumnsDeclareAlignmentTests` guards it.
- Center places a column correctly but sizes it to its content, which left the three
  widest pages narrower than Dashboard. Weekly Digest, Knowledge Vault and Model Manager
  also bind the column's MinWidth to `ContentColumn.Fill(PageScroller.ViewportWidth, 1200)`
  (`src/AgentX.App/Helpers/ContentColumn.cs:20`), the viewport capped at the column's
  MaxWidth: as wide as the cap allows, never wider than the viewport, and centered.

**Guards** (`tests/AgentX.Tests/CodeQuality/`)
- `NavRailParityTests`: no shared rail glyphs, no headerless group, every item localized
  and in both page maps, and the palette holds no literal page list.
- `NoOrphanKeyedResourcesTests`: every keyed resource in the style dictionaries has a
  consumer or a named DESIGN.md reason (comment-stripped, chain-aware; Fluent lightweight
  styling keys allowed by prefix).
- `SpacingIsOnTheFourPixelGridTests`: XAML spacing attributes and code-behind Thickness.
- `MachinedRadiusStopsTests` now also scans code-behind `new CornerRadius(...)`.
- `NoBannedPaletteHuesTests` exempts only `AnnotationInk.cs` and checks its contents.
- `ShortcutCatalogTests` and `CommandPaletteViewModelTests` cover the intent on Ctrl+N,
  the registry-read chord hints, and scope switching.

**Tooling**
- `scripts/uia-nav-smoke.ps1`: per-page UI Automation navigation smoke in one or both
  shifts, anchored on each page's own XAML names and text, with a palette check and
  foreground-asserted captures.

### Changed - Hex socket cap bolts read as machined stainless (2026-09-06)

Rocky's note: the bolts lay flat and the dark colour washed out. They did. The Night
cap ramp ran `#3A3A3A` to `#0E0E0E` (`src/AgentX.App/Styles/Hardware.xaml:102`), which
is three greys with no specular anywhere, so a bolt was a slightly-lighter disc on a
dark plate rather than a turned piece of metal sitting proud of it.

What a socket cap screw actually shows, and what the bolts now render:

- A specular hotspot where the light hits, falling through a steel body to a shaded
  lower-right. Five stops instead of three, lit from the upper left.
- A machined rim. The turned edge catches light on the lit side and drops into shadow
  on the other, and this single ring is what separates the cap from the faceplate
  (`Styles/Hardware.xaml:112`, new `BoltRimBrush`).
- A recess with real depth. The Allen socket was a flat fill; it is now a gradient with
  the near wall in shadow and the far wall picking up bounce, plus a chamfer highlight
  on the lower lip only.
- A softer countersink: the halo was a hard black disc and is now a ring that fades out.
- The socket grew from 41% to 54% of head diameter so the six flats read at 20px. At the
  old size the hexagon resolved to a dot on the dark shift.

Day Shift gets the same construction against silver. HighContrast is untouched and stays
flat and system-bound, per `DESIGN.md:253`; the two new keys are declared there as
`SystemColor*` solids so the theme never sees a gradient.

### Changed - Cards now use the Layer 3 raised-control recipe (2026-09-06)

`DESIGN.md:69` specifies raised controls as a vertical gradient with a top highlight, and
`DESIGN.md:265` bans flat elevation outright. The recipe existed, fully built for all
three themes as `ControlCapBrush`, and had no consumers at all; cards meanwhile drew on
`CardGradientBrush`, which Colors.xaml itself labels a flat solid. This is the same
unwired-not-missing pattern as `DropZoneActiveStyle`.

`CardStyle`, `CardElevatedStyle`, `CardInteractiveStyle`, `DocumentGridItemStyle` and
`DocumentSimpleCardStyle` now draw on the cap recipe
(`src/AgentX.App/Styles/Controls.xaml:112`). That is 59 `CardStyle` call sites alone.
Verified on the Operations page in both shifts.

### Fixed - Two radius tiers had collapsed into one (2026-09-06)

DESIGN.md cuts four machined stops: plates 2, caps 4, overlays 8, circles 9999
(`DESIGN.md:183`). The rule underneath them is "NEVER uniform radius across surface
types", so the failure mode is not an exotic value, it is tiers quietly merging. Both
had happened.

- `RadiusMD` resolved to 8, the overlay stop, and 111 content Borders across the views
  referenced it. Cards and dialogs were being cut identically. Checked before moving it:
  no dialog or flyout uses `RadiusMD` at all, since overlays reference `ROverlay`
  directly, so the alias was purely a content-surface token. Re-pointed to the cap stop
  (`src/AgentX.App/Styles/Colors.xaml:726`), which re-cuts all 111 through the token
  layer at once.
- Twenty-three style definitions across `Controls.xaml`, `Documents.xaml` and `Chat.xaml`
  set an overlay-tier radius on Layer 3 raised controls: cards, ghost buttons, list
  items, badges, inline bars, the drop zone, the file-type icon container. All re-cut to
  `RControl`. `PremiumToolTipStyle` deliberately kept `ROverlay`, because a tooltip is a
  flyout.
- `RadiusLG` and `RadiusXL` now have zero consumers, completing the retirement
  `DESIGN.md:189` called for. The keys remain so any straggler still lands on a real stop.
- The lamp tint bled at a hardcoded 3 while the cap it tints sits at 4
  (`src/AgentX.App/Controls/LampTile.xaml:36`).

`tests/AgentX.Tests/CodeQuality/MachinedRadiusStopsTests.cs` guards both halves: every
radius token resolves to a stop, and no view hardcodes one off the scale. `Colors.xaml`,
`Hardware.xaml` and `Generic.xaml` are exempt, since those hold the token definitions and
the hardware recipes DESIGN.md specifies literally.

Verified against the running app in both shifts: composition unchanged, faceplates still
at the plate stop, cards visibly tighter.

### Fixed - The app was shipping four palettes (2026-09-06)

A hue audit of every color literal in `AgentX.App` found three foreign palettes living
alongside the Command Console one. None of them were in the token file, which is why the
Tier 3 sweep missed them: they arrived one literal at a time in code-behind and view models,
where each looked local and reasonable.

- **One Dark Pro**, in the code-block highlighter. Its keyword color was `#C678DD`, a purple
  that `DESIGN.md:263` bans by name, and its call color was a `#61AFEF` blue the system does
  not admit at all. Retuned to documented tokens
  (`src/AgentX.App/Helpers/SyntaxHighlighter.cs:30`). Armed red is deliberately absent from
  the new mapping: red is this product's LIVE signal, and spending it on the word `if` would
  dilute the one hue that means the machine is working. Comments moved from `#5C6370` to
  `SilverMute`, which raises them from roughly 2.6:1 to about 5.3:1 on the void well.
- **Material**, in the search relevance meter. `#4CAF50` / `#FFC107` / `#FF9800` / `#F44336`
  became the LED segment cascade (`src/AgentX.App/Views/SearchPage.xaml.cs:304`). A second
  copy of the same ramp lived on `SearchViewModel.ScoreColor` with no consumer anywhere; the
  live path is the code-behind one bound at `SearchPage.xaml:673`, so the duplicate was
  removed rather than recolored.
- **Tailwind**, as `#22C55E` on eighteen check glyphs across the user guide, now
  `SuccessBrush` (`Styles/UserGuideSections.Features.xaml`, `.Research.xaml`, `.Advanced.xaml`).

`tests/AgentX.Tests/CodeQuality/NoBannedPaletteHuesTests.cs` guards the rule by hue rather
than by a list of known-bad hex values, because the next violation will be a hex nobody has
written down yet. Annotation ink is exempt with a stated reason: those five color names are a
persisted contract (`src/AgentX.Core/Data/Entities/AnnotationEntity.cs:47`), so a highlight
the operator saved as "purple" has to render purple. That is user content, not chassis.

### Fixed - Remaining hardcoded surfaces, one unwired style, one dead style (2026-09-06)

- `MarkdownMessageControl` hardcoded all seven of its surfaces and text colors, including an
  off-palette `#E58684` for inline code. They now resolve through the well family, which is
  identical in both shifts and system-bound in HighContrast; the code stream sits on `Void`,
  which `DESIGN.md:114` names for exactly that
  (`src/AgentX.App/Controls/MarkdownMessageControl.xaml.cs:191`).
- `DropZoneActiveStyle` was never dead, it was unwired. The drag-over handler carried the
  comment "apply active drop zone style" and then hand-rolled the brushes instead
  (`src/AgentX.App/Views/KnowledgeVaultPage.xaml.cs:166`). Checked for a waiting consumer
  before touching it, which is what turned up the mismatch.
- `DocumentCardStyle` had no consumer in markup or code and its grid uses
  `DocumentGridItemStyle`; removed after the same check.
- The search no-results state wore NO-GO, which `DESIGN.md` reserves for terminal faults. A
  query that matched nothing is informational, so it now wears LedScope
  (`src/AgentX.App/Views/SearchPage.xaml:563`).

### Fixed - Code-behind brushes ignored the shift, breaking Day Shift and HighContrast (2026-09-06)

`ThemeService` applies a shift by setting `RequestedTheme` on the window root
(`src/AgentX.App/Services/ThemeService.cs:71`), but an
`Application.Current.Resources["Key"]` lookup resolves ThemeDictionaries against the
application's own theme, which the root never updates. Every brush pulled that way in
code-behind was frozen at its Night Ops value. `DESIGN.md` had already recorded this exact
trap once, in the 2026-07-05 decision that made `StatusToColorConverter` shift-aware; these
were the second and third occurrences.

The keys involved are each defined three times in `src/AgentX.App/Styles/Colors.xaml`, once
per ThemeDictionary (`Default` at :52, `Light` at :236, `HighContrast` at :422), which is
what made the lookups wrong. Two lookups in the same sweep were correct and were left alone:
`RedGlow10` (`Colors.xaml:639`) and `Red500` (`:628`) are declared once, outside the
ThemeDictionaries block, so they carry no per-shift value.

- Eighteen lookups across five files now resolve against the root's `ActualTheme` through
  `src/AgentX.App/Helpers/ThemeResources.cs`, which also treats system high contrast as its
  own dictionary so the hardware skin cannot leak into a theme `DESIGN.md:253` calls
  untouchable.
- The Ctrl+K command palette was the visible casualty: it built its rows in code
  (`src/AgentX.App/Controls/CommandPalette.xaml.cs:404`), so on Day Shift every command
  rendered Night-white on a silver faceplate. Its hardcoded `#1A1A1A` selected-row and
  shortcut-chip fills are replaced by `CardHoverBrush` (follows the shift) and `VoidBrush`
  (dark in both shifts, per the displays-stay-dark rule), matching the sibling chord hints
  already in `CommandPalette.xaml:117`. The chip also moved from Iosevka to Departure Mono,
  which `DESIGN.md:93` assigns to kbd hints.
- `KnowledgeVaultPage` reset its drop zone by re-resolving brushes; it now clears the local
  values (`src/AgentX.App/Views/KnowledgeVaultPage.xaml.cs:179`) so `DropZoneStyle`'s
  `{ThemeResource}` setters resume and keep updating on a live shift change.
- `tests/AgentX.Tests/CodeQuality/ThemeVaryingBrushesAreResolvedPerRootTests.cs` guards the
  class. It reads the theme-varying key set out of `Colors.xaml` rather than hardcoding it,
  so it tracks the design system instead of a snapshot of it.

### Fixed - Off-palette hues and off-canon radii that survived the Tier 3 sweep (2026-09-06)

- Tailwind red `#EF4444` was still filling two surfaces, the plugin danger zone
  (`src/AgentX.App/Views/PluginManagerPage.xaml:731`) and the search no-results badge
  (`src/AgentX.App/Views/SearchPage.xaml:560`). Both now use `ErrorSubtleBrush`, which is
  the `LedNoGo` family and is defined in all three themes.
- The Smart Inbox web-clip badge put white text on `InfoBrush`
  (`src/AgentX.App/Views/InboxPage.xaml:257`), about 1.6:1 in Night Ops. It now renders the
  LED tone on a subtle tint, the same recipe as the chat badges at `ChatPage.xaml:824`.
- `RadiusLG` and `RadiusXL` still resolved to 12 and 16, which are not machined stops.
  `DESIGN.md:189` retires both to `ROverlay`; they are now 8
  (`src/AgentX.App/Styles/Colors.xaml:724`). Keys are preserved so consumers re-cut through
  the token layer, the same mechanism the cardinal-to-armed color migration used.
- Stray literal radii re-cut to tokens: two Analytics trend bars to `RCard`
  (`src/AgentX.App/Views/AnalyticsPage.xaml:1160`) and an Onboarding status badge to
  `RControl` (`src/AgentX.App/Views/OnboardingPage.xaml:485`).

### Fixed - Email triage category was deleted instead of wired (2026-08-24)

`EmailCategory` was an eight-member enum that nothing assigned and nothing read, so the
2026-08-23 dead-code pass removed it. That read the symptom, not the defect: the field it was
meant to fill already existed, was already persisted, and was already on screen. Every email the
connector imported was filed under the constant `"email_message"`, so the Smart Inbox source
column said "Email Message" for all of them and told the reader nothing.

- `EmailTriageProcessor.Classify` assigns a category from the subject, body and sender
  (`src/AgentX.Core/Services/Plugins/Email/EmailTriageProcessor.cs:58`). The rules are ordered and
  the first match wins; urgency is checked first, because a message asking the reader to act is
  what triage exists to surface, and the sender-based rules are checked last, because an
  unattended address says the least about what a message is for. Keyword matching is word-bounded,
  so "Salesforce" is not a sale and "idealized" is not a deal.
- The selected member's name is written to `InboxItemEntity.SourceCategory`
  (`EmailTriageProcessor.cs:42` to `EmailSyncService.cs:110` to `InboxService.cs:679`), which is
  exactly what that column's own documentation already described
  (`src/AgentX.Core/Data/Entities/InboxItemEntity.cs:96`, whose example value is
  `"ActionRequired"`).
- The Operations page renders it with no change: `BuildInboxSourceLabel` already title-cased the
  stored value (`src/AgentX.App/Services/OperationsOverviewService.cs:588`), so an item now reads
  "Action Required" or "Newsletter" instead of "Email Message".
- Covered by `tests/AgentX.Tests/Services/Email/EmailCategoryClassificationTests.cs`: one test per
  category, three precedence tests, two word-boundary tests, and a reflection test that drives the
  real Operations label helper.

### Added - The sweep's dismissal record now reaches a checkout (2026-08-24)

`.claude/verify-ignore` holds the reason each verification-sweep finding was dismissed, and
`.gitignore` excluded the entire `.claude/` directory, so none of it was ever committed. A fresh
clone saw the full finding list with no way to tell a settled finding from a new one.

- `.gitignore` now ignores `.claude/*` with an explicit exception for `verify-ignore`, and the
  file is committed with a header stating the policy.
- `tests/AgentX.Tests/CodeQuality/VerifySweepSuppressionsTests.cs` enforces it: the file is
  tracked by git, every entry states a reason, every entry still points at a path that exists, and
  every entry filed under "XAML-only references" is proven to appear in real markup. That last
  check turns the largest block of dismissals from a claim into a receipt.
- `.github/workflows/build-test.yml` now triggers on changes to the record.

### Fixed - Two detectors in the shared verification tooling misfired (2026-08-24)

Both live in the shared hook library `verify-lib.mjs` and are covered by its regression suite
(`run-tests.mjs`, 72 checks).

- **Gate 4 only ever saw three file names.** `isDocFile()` selected by basename prefix, README /
  CHANGELOG / RELEASE, so every other markdown file was outside the doc-claim check entirely:
  `ARCHITECTURE.md`, `USER-GUIDE.md`, `API-REFERENCE.md`, the whole `docs/` tree. Those are exactly
  the pages a false claim survives in, because they are long and nobody re-reads them. Markdown is
  now selected by extension, with the name prefixes kept so a `RELEASE-NOTES.txt` keeps its
  coverage. Running the sweep with the fix in place immediately surfaced the documentation defects
  fixed below.
- **`WaitForExit(` was read as a disabled test.** The forced-green marker list matched `xit(` as a
  raw substring, so any test calling `Process.WaitForExit(` or `Environment.Exit(` was reported as
  fake. `xit` and `xdescribe` are now matched as whole identifiers.

### Fixed - Documentation claims that contradicted the code (2026-08-24)

Found by the Gate 4 fix above. Each was checked against the running code or the live release
before being rewritten.

- **"2-50x inference speedup"** appeared twice in `docs/README.md` (lines 13 and 99) and had never
  been benchmarked. Replaced with the offload tiers the code actually applies
  (`src/AgentX.Core/AI/Models/AiModel.cs:119`) and an explicit statement that throughput is not
  measured here. The same removal covers the invented per-tier speedup table in
  `docs/user-guide/faq.md` and the "Expected Speedup: 20-30x" line in the quick-start guide.
- **A GPU settings panel that does not exist.** `docs/user-guide/getting-started/quick-start.md`
  drew an ASCII mock of "Settings, AI Runtime, GPU Acceleration" with VRAM-tier checkboxes, and
  the FAQ and troubleshooting guides told users to enable it there. There is no AI Runtime section
  and no GPU control anywhere in `src/AgentX.App/Views/SettingsPage.xaml`; the value the app reads
  is `LocalGpuLayers`, which defaults to 0 (`src/AgentX.Core/Services/Settings/AppSettings.cs:21`)
  and is consumed only at `src/AgentX.Core/AI/AiService.cs:80`. All three pages now describe the
  settings-file path that actually works.
- **"Ships with the model bundled in the installer."** Corrected at `docs/README.md:61`, `:78` and
  `:203`, and in `docs/user-guide/faq.md`. That is true only of the OFFLINE profile; the default
  SLIM profile downloads the weights on first run (`installer/AgentX-Setup.iss:8`). Line 9 of the
  same file had already been corrected in the previous pass, so the file contradicted itself.
- **Installer profiles on the current release, verified.** The published release is v2.1.1. Its
  only GitHub asset is `AgentX-Setup-2.1.1-x64.exe` at 238,817,423 bytes (SLIM); the OFFLINE build
  is live at `downloads.strategia-x.com` at 2,225,422,354 bytes and is linked from the release
  notes. `docs/README.md:198` had told users to download 2.1.2 files from the releases page, where
  no 2.1.2 release exists.
- **"One-time purchase, perpetual use"** in the FAQ comparison table contradicted the same file's
  own "100% free and open-source, nothing to buy" answer four lines later.
- Stale unit-test counts (2,835) updated to the measured 2,996 in `README.md` and `docs/README.md`.

### Fixed - The import picker offered two formats nothing could read (2026-08-24)

The Knowledge Vault picker advertised `.rtf` and `.htm`. Neither was claimed by any
`IDocumentProcessor`, so selecting one of those files failed as "unsupported format" after
the user had already chosen it. This is the mirror image of the unregistered-processor
defect: there a capability existed with no route to it, here a route existed with no
capability behind it.

- `.htm` was an omission and is now handled. It is the same format as `.html`, which
  `CodeFileProcessor` already read, so it was added to the canonical
  `SupportedFileTypes.Code` set (`src/AgentX.Core/Documents/Models/ProcessedDocument.cs`)
  and to the language map (`src/AgentX.Core/Documents/Processors/CodeFileProcessor.cs:46`).
- `.rtf` was a promise with nothing behind it. No processor reads RTF, so the picker no
  longer offers it (`src/AgentX.App/Views/KnowledgeVaultPage.xaml.cs`).
- `tests/AgentX.Tests/CodeQuality/ImportPickerOffersOnlyProcessableTypesTests.cs` compares
  the picker's filter list against every extension the processors declare, and fails on any
  entry with nothing behind it.

### Fixed - Documentation claims anchored or corrected, second pass (2026-08-24)

Running the repaired Gate 4 over the files this change touched surfaced more claims that no
code supported.

- **The supported-formats table in `docs/user-guide/faq.md` was wrong in both directions.** It
  advertised RTF, which nothing reads; it presented legacy `.doc` as equal to `.docx`, when
  `DocxProcessor.cs:15` states the binary format is not natively supported; and it omitted
  images, audio and web bookmarks, which are supported. The table now lists each format
  against the processor that claims it.
- **"Multi-GPU support is planned for a future release"** was roadmap written in the present
  tense. There is no multi-GPU path in the code; the answer now says so and points at
  `src/AgentX.Core/AI/AiService.cs:80`.
- The licensing statement appeared three times in `docs/README.md` and twice in the FAQ, once
  still repeating the corrected bundled-model claim. All five now point at `LICENSE`.
- Countable claims in `docs/README.md:12` (navigation pages, services, tests, REST API) each
  carry the file that proves them; the unit-test count is the measured 3,006.
- Anchors added for the onboarding wizard, the three runtime identifiers
  (`src/AgentX.App/AgentX.App.csproj:9`), the CUDA 12 runtime
  (`LLamaSharp.Backend.Cuda12`), organisation methods, and the AI providers.

### Added - Coverage for the ingest path, and a ratcheted floor (2026-08-24)

The branch floor had been left at 51 while the measured value drifted toward it. A gate with a
fraction of a point of headroom fails on the next unrelated commit, and a gate that fails for no
reason is one people start overriding. Three uncovered classes were closed instead.

- `ChunkingService` (218 lines, 0%), the recursive paragraph/sentence/word splitter behind every
  embedded chunk. `tests/AgentX.Tests/Documents/ChunkingServiceTests.cs`.
- `AdaptiveChunkingService` (115 lines, 0%), the classifier that silently overrides the caller's
  chunk size for code and tables.
  `tests/AgentX.Tests/Documents/AdaptiveChunkingServiceTests.cs`.
- `DocumentDisplayDto` (26 lines, 109 branches, 0%), the file-type icon switch behind every
  document list. `tests/AgentX.Tests/DTOs/DocumentDisplayDtoTests.cs`.
- Global coverage moved 64.17 to 65.42 line and 53.31 to 56.29 branch; the floors in
  `scripts/check-coverage.ps1` were ratcheted 62 to 65 and 51 to 55, leaving the branch gate about
  1.3pt of headroom rather than a fraction of a point. The `AgentX.Core.Services.Backup` floor is
  unchanged at 75/65: it measured 79.21 this round, the top of its known band, and its residual is
  the deliberately uncovered restore-swap body.


### Fixed - Code-quality audit: unreachable features and inert controls (2026-08-23)

A full-codebase audit for stubs, placeholder data, and unwired modules. Several features were
fully implemented and tested in their view models but had no way to reach them from the running
app: the control existed but invoked nothing, or no control existed at all. These are now wired,
and four structural tests keep the whole class of defect from returning.

- **The chat model picker did nothing.** The header ComboBox listed available models but never
  reported the selection back, so choosing a model silently kept the previous one. Selecting a
  model now activates and persists it.
- **Smart Inbox triage was inert.** Accept, Defer, and Reject rendered with tooltips and
  accessible names but had no handler, so the queue could not be worked. The status filters had
  the same problem. All four are now wired.
- **Annotations could not be edited or deleted.** The delete button invoked nothing and the whole
  edit flow (`EditAnnotation` / `SaveAnnotation` / `CancelEdit`) had no control anywhere in the
  page. Added an edit panel and wired delete and the color filters.
- **Workflow steps could not be reordered or removed.** Move Up, Move Down, and Remove Step were
  all inert.
- **Backup history could not be restored from a row.** The per-backup restore button had no
  handler; it now runs through the same confirmation gate as the page-level restore.
- **Collections could not be renamed.** `RenameCollection` looked the collection up, logged
  "rename requested", and returned without calling the service. It now persists the new name.
- **Bulk operations had no UI.** Multi-select, Select All, and bulk delete for collections, and
  bulk enable / disable / uninstall for plugins, were implemented and tested but unreachable.
  Both pages gain a multi-select mode with per-row checkboxes and a bulk action bar
  (`src/AgentX.App/Views/CollectionManagerPage.xaml:150`,
  `src/AgentX.App/Views/PluginManagerPage.xaml:165`).
- **Jump-To discarded what you picked.** Selecting a specific document or conversation opened the
  generic Knowledge Vault or Chat page instead of that item. Navigation now carries a payload, and
  the target pages honour it.
- **Dashboard search discarded the query.** Typing in the dashboard search box and submitting
  landed on an empty Search page. The box now submits on Enter and carries the query through.
- **The Auto-Sync toggle did not stop the sync loop.** Switching it off changed a flag while the
  background loop kept running, so the control reported a state it did not enforce.
- **Belief conflicts could not be acknowledged.** The dashboard surfaced conflicts with no way to
  dismiss them, so an acknowledged conflict reappeared on every launch.
- Conversation export gained the routes its view model already implemented: **Copy as Markdown**
  in the export dialog and **Export all conversations** from the chat sidebar. Collections gained
  a per-collection export.
- Backup size estimate can be recalculated, and sync history can be reloaded, without leaving the page.

### Fixed - Honest status reporting

- The dashboard reported **100% indexed / "Idle"** when the indexing query threw, presenting a
  healthy reading for a state it never observed. It now reports "Status unavailable".
- The Past Self voice profile showed a **15-word average sentence length and a "Balanced" style
  with zero samples captured** — invented statistics indistinguishable from real measurements. An
  empty profile now says so.

### Fixed - Correctness and accessibility

- Citation file-path lookup in Ask Your Files blocked on an async call with `Task.Wait()` /
  `Task.Result` inside the answer pipeline, risking a UI-thread deadlock. It is now awaited.
- The Database Encryption section of Settings used WinUI's default typography instead of the
  Command Console type styles used by every other section on the page.
- **Every visible interactive control now has an accessible name.** 68 controls across 13 files
  exposed none, so a screen reader announced them as unlabelled buttons with nothing to tell
  them apart. WinUI only derives a name from string `Content`, and these hold an icon plus a
  label, or an icon alone.
  - 57 icon-plus-label controls now point at the label they already render
    (`AutomationProperties.LabeledBy`), and 4 date pickers point at their own captions. This
    reuses the existing localized text, so no string was duplicated into resources.
  - 24 icon-only controls take their name from the tooltip they already carry, mirrored into
    `AutomationProperties.Name` across all six locales. UIA exposes a tooltip as help text
    rather than as the name, so the tooltip alone never reached a screen reader as an identity.
  - 3 controls with neither a label nor a tooltip received newly translated names, one of which
    (the notification dismiss button) also had a hardcoded English tooltip.
  - A UI Automation sweep of all 29 pages reports 296 named controls, and 7 that the sweep
    still found unnamed. See the follow-up entry below: those 7 were on screen, not collapsed,
    and the guard was passing them.

- **The accessible-name guard was passing controls that have no name.** A control carrying an
  `x:Uid` was treated as named on the strength of the uid alone, without checking that the
  resource file defines a name under it. Twenty-one buttons whose uid holds only a
  `.ToolTipService.ToolTip` entry were skipped on that basis: UIA exposes a tooltip as help
  text and never as the name, so a screen reader reached them with nothing to announce. The
  guard also scanned only the four button types, so `RadioButton`, `CheckBox`, and
  `ToggleSwitch` were never examined at all.
  - A UI Automation sweep found 12 of these live and on screen, none of them collapsed: the
    Search and Advanced buttons on Semantic Search, New / Prompt / Folder on AI Chat, Select in
    the Knowledge Vault, Refresh on the Knowledge Graph, and all five Quick Actions tabs.
  - 27 icon-plus-label controls now point at the label they already render, and the document
    row checkboxes on Compare Documents and in the Knowledge Vault point at the file name they
    select, so the announced name is the document rather than a bare "check box".
  - Three toggle switches announced their state instead of their purpose, because a
    `ToggleSwitch` with no header falls back to its on/off caption: Settings' encryption switch
    announced "Not encrypted" and collaborative sync announced "Off". They now point at the
    section heading beside them. The plugin detail switch, which has no heading, received a
    translated name in all six locales.
  - The guard now resolves every `x:Uid` against `Resources.resw` and requires a name-bearing
    entry, covers the eight interactive control types, and names the reason for each offender.
    `scripts/name-unlabelled-controls.py` carries the same rule, so the paved road no longer
    reproduces the gap; it also seeds its uniqueness check from names already in the file,
    which previously let a second run mint a duplicate `x:Name`.
  - Verified by re-running the UI Automation sweep against the built app: 0 unnamed controls
    across all 29 pages.

### Removed - Dead code

- `WorkspaceService` (821 lines): a complete parallel multi-workspace subsystem with its own
  JSON metadata store and per-workspace database, never registered, never referenced, and never
  tested. It duplicated no live behaviour — the shipped feature is `WorkspaceProfileService`.
- Six unreachable duplicate commands whose behaviour is already provided by a working path:
  chat regenerate and research-mode toggles, two chat export commands superseded by the export
  dialog, a workflow export command that discarded its own result, and a sync folder picker whose
  event nothing subscribed to.

### Fixed - Two document formats were implemented but never registered (2026-08-23)

A second audit pass, driven by a whole-repository wiring sweep, found the same defect class in
the composition root rather than in XAML. `DocumentService` resolves processors only as
`IEnumerable<IDocumentProcessor>` (`src/AgentX.Core/Documents/DocumentService.cs:32`) and
selects one by extension (`:761`); there is no reflection-based discovery. A processor is
therefore absent from the product unless `App.xaml.cs` registers it, no matter how complete
or well-tested it is.

- **Audio files could not be imported.** `AudioProcessor` (.mp3, .wav, .m4a, .flac, .ogg,
  .webm) was fully implemented against `ITranscriptionService` and never registered, so audio
  import fell through to "unsupported format" while the README advertised the capability. Now
  registered at `src/AgentX.App/App.xaml.cs:480`.
- **URL shortcut files could not be imported.** `WebProcessor` (.url, .webloc) had the same
  problem. Now registered at `src/AgentX.App/App.xaml.cs:481`.

### Added - Tests for the two newly-reachable processors

- `WebProcessorTests` (24 tests) and `AudioProcessorTests` (22 tests) cover the code that
  registration just made reachable: .url INI parsing, .webloc plist parsing, malformed and
  empty inputs, URL validation, scraper failure and fault paths, transcript assembly, speaker
  diarization counting, and each degraded transcription arm (Whisper runtime missing, model not
  downloaded, cancellation, unexpected fault). Only the network and transcription boundaries are
  mocked; both suites run against real files on disk. Each was break-checked by inverting
  conditions in the processor and confirming the tests fail.

### Added - Guard test for collection-resolved services

- `EveryCollectionServiceIsRegisteredTests` fails when a concrete `IDocumentProcessor` or
  `IExportFormatter` in `AgentX.Core` has no registration line in `App.xaml.cs`. These
  interfaces are resolved only as `IEnumerable<T>`, so a missing registration is invisible to
  every other signal a developer reads: it compiles, it unit-tests, and it reports as covered.
  The guard also asserts its own scan is non-empty, so it cannot silently pass forever.

### Removed - Dead code, second pass (2026-08-23)

Nineteen files with zero inbound references anywhere in the repository. Each was verified
unreferenced before removal, and the full suite plus a clean `dotnet build -p:Platform=x64`
confirmed nothing depended on them.

- **Five unused value converters**: `BytesToStringConverter`, `CitationSourceColorConverter`,
  `PercentToHeightConverter`, `TokensToStringConverter`, `ZeroToVisibleConverter`. Three of
  them were documented in `docs/ARCHITECTURE.md` as if in use; that table now lists the twelve
  converters actually referenced from XAML.
- **`MarkdownExport`** and its test: superseded by the registered `MarkdownFormatter`. Its
  siblings `HtmlExport` / `JsonExport` / `PdfExport` stay, each wrapped by a registered
  formatter.
- **Six unused exception types**: `EntityNotFoundException`, `ExportException`,
  `PluginException`, `SyncException` (with `SyncErrorType`), `InvalidDatabaseKeyException`,
  and `PendingMigrationsException`. None was ever thrown. The passphrase-retry behaviour that
  `InvalidDatabaseKeyException` described is implemented without it, as a bool return in
  `App.xaml.cs` `TryProbeKeyAsync`; the interface comment that told callers to throw it now
  describes what the code actually does.
- **Four unused DTOs**: `MessageDto`, `DashboardRecentDocumentDto`,
  `DashboardRecentConversationDto`, `FileTypeBreakdownDto`.
- **`FeedSubscriptionEntity`**: an entity with no `DbSet` on `AgentXDbContext`.
- **`EmailCategory`**: an enum for AI email triage that nothing assigned or read. The
  `SourceCategory` string on `EmailTriageProcessor` is an unrelated provenance tag.
- **`CollectionPickerItem`** and **`DispatcherQueueExtensions`**: neither had a call site.

### Fixed - Documentation claims that no longer matched the code (2026-08-23)

- `docs/README.md` said the installer ships the Llama 3.2 3B weights. The default SLIM profile
  downloads them on first run (`installer/AgentX-Setup.iss:8,129`); only the OFFLINE profile
  bundles them. Both paths are now described.
- `docs/README.md` reported a stale unit-test count.
- `docs/DEVELOPER-GUIDE.md` claimed the migration runner raises `PendingMigrationsException`
  and halts startup. It never did: `MigrationRunner.cs:298` calls `MigrateAsync()` and lets EF
  Core exceptions propagate.
- Removed the `EmailCategory` reference section from `docs/API-REFERENCE.md`.

### Added - Structural guard tests

- `NoUnwiredInteractiveControlsTests` fails when a button, menu item, or similar control ships
  with no Click handler, Command binding, or flyout. WinUI silently absorbs presses on such a
  control, so nothing else catches it.
- `NoUnreachableViewModelCommandsTests` fails when a `[RelayCommand]` cannot be invoked from any
  view or code path — implemented, covered by tests, and still unreachable for the user.
- `NoUndefinedXamlResourceKeysTests` fails when XAML references a `StaticResource` or
  `ThemeResource` key nothing defines. These resolve at page realization, so a bad key builds
  clean and throws the first time a user opens the page.
- `InteractiveControlsHaveAccessibleNamesTests` fails when a control has no name for assistive
  technology. Explicitly disabled controls are exempt, since they are never focusable.

Two reusable scripts came out of this: `scripts/name-unlabelled-controls.py` (points controls
at their own visible label) and `scripts/mirror-tooltips-to-automation-names.py` (copies an
existing translated tooltip into the automation name).

### Changed - Command Console redesign (2026-07-05)

The entire UI adopts the **Command Console design system** from the Strategia family, defined in [`DESIGN.md`](DESIGN.md) (the new source of truth for all visual work):

- **Armed red `#AA2024`** replaces the cardinal `#C41E3A` accent across all 29 views, with a full LED status vocabulary (go green, hold amber, warn red, no-go red, scope teal) replacing ad-hoc status colors.
- **Four bundled typefaces** (SIL OFL, no runtime downloads): Public Sans for body text, Archivo Expanded for stencil placards and page titles, Departure Mono for telemetry readouts, Iosevka Term for streams and code (retiring Cascadia Code).
- **Hardware depth**: pages are built from faceplate modules with machined kickers and corner bolts; metrics render in recessed LCD wells with phosphor glow; buttons are machined and armed caps with physical press travel.
- **Instrument strip**: the bottom status bar is reborn as a live instrument row - `MDL` model lamp and readout, `IDX` embedding-queue and `VAULT` document-count LCDs, the `LOCAL`/`NET` privacy lamp, and a new annunciator cluster (`INBOX`/`SYNC`/`JOBS`/`BAK`) fed by a typed aggregation service. Lit lamps navigate to their source page; blinking lamps acknowledge on click.
- **Three themes**: dark (Night Shift, default), light (Day Shift, brushed silver), and high contrast (bound to system colors, exempt from the hardware skin). Display surfaces stay dark in both regular themes by design. Status text tones are shift-aware for contrast on light surfaces.
- **Reduced motion respected**: lamp strikes, meter ballistics, and cap travel snap instantly when Windows animations are disabled.

### Added

- All UI now available in six languages (en-US, de, es, fr, ja, zh-CN): every page, dialog, MainWindow chrome, and the Privacy/Terms pages are x:Uid-instrumented with full resw key parity, enforced by the LocaleAudit CI gate (2026-07-04).
- `SegmentMeter` control: a 12-segment instrument meter with zone tones and needle ballistics, driving the Dashboard indexing gauge and the Hardware Advisor RAM gauge.

### Fixed

- **Theme choice now persists across restarts.** The theme was saved under a key that matched no settings property, so every launch silently reverted to dark.
- **Model Manager connection dot and Sync Settings state dot now reflect live state.** Both were initialized green and never updated, showing "go" even when disconnected or errored.
- Quick Actions page could fail to open due to a tab handler firing during XAML parse (2026-07-04).
- Databases with a stamped baseline but missing tables now self-heal at startup instead of bricking the app (2026-07-04).

## [2.1.2] — 2026-06-21 — "Bedrock" security & supply-chain hardening

Security and hardening release. Closes the full **Codex security audit** and the **Comprehensive QA Audit (2026-06-19)** — every finding **AX-QA-001 through AX-QA-016** — makes the release pipeline signing-ready, and brings the mobile companion to a verified build. No breaking changes; no database schema changes.

### Security

- **Local REST API now requires authentication.** The desktop API (Enhancement #16) previously served unauthenticated loopback requests; it now enforces a bearer token, and the mobile companion authenticates against it (Codex audit).
- **File-access boundaries hardened.** Local-API path handling is contained to approved roots (`ResolveContainedPath`), closing directory-traversal exposure (Codex audit).
- **Secrets encrypted at rest** rather than stored in plaintext configuration (Codex audit).
- **Mobile transport hardened (AX-QA-005).** Removed the `DangerousAcceptAnyServerCertificateValidator` TLS bypass; plaintext HTTP is refused to any non-loopback host; added an optional pairing-established SPKI-SHA-256 certificate pin with constant-time comparison. See [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md).
- **Dormant vulnerable SQLite binary removed (AX-QA-010).** Switched `AgentX.Core` and the test project to the `Microsoft.Data.Sqlite.Core` / `Microsoft.EntityFrameworkCore.Sqlite.Core` packages, so the unmaintained `e_sqlite3` native binary (GHSA-2m69-gcr7-jv3q) no longer ships; the app continues to run on the SQLCipher provider (`bundle_e_sqlcipher`). The dependency-audit allowlist is now empty.

### Fixed

- **Fresh-install / partial-baseline self-heal (AX-QA-002, AX-QA-003).** The migration runner detects and repairs a partially-stamped baseline, and startup is now fail-closed so a half-initialised database can no longer surface a broken UI; closed a dashboard-load-vs-migration race via `IStartupGate`.
- **Dashboard privacy claim is state-aware (AX-QA-008).** The "no cloud" assurance reflects the actual provider state through `IPrivacyStatusService` instead of being hard-coded.
- **Knowledge-vault document-reload race eliminated (AX-QA-009)** in `KnowledgeVaultViewModel`.
- **Mobile Android build is green (AX-QA-004).** The MAUI companion now builds clean for `net8.0-android` (Debug **and** Release, 0 warnings) and is a **blocking** CI gate. It had been compile-unverified due to a missing Android platform head (now scaffolded: manifest, `MainActivity`/`MainApplication`, icon/splash) and a wrong-API call in `MauiProgram.cs` — a non-existent parameterless `UseMaui()`, corrected to the canonical `UseMauiApp<App>()`.
- **Single-source version display (AX-QA-014).** The dashboard footer, Settings page, and backup manifest now read one assembly-backed version (`AppVersionInfo`) instead of three drifting hard-coded strings.
- **Browser extension (AX-QA-013, AX-QA-015).** Long recent-clip titles/URLs truncate with an ellipsis; the feedback area is an ARIA live region announced to assistive technology (escalating to `assertive` for errors).

### Added / Changed — release engineering

- **Signing-ready installer pipeline with provenance gate (AX-QA-001, AX-QA-007).** `scripts/build-installers.ps1` Authenticode-signs and RFC-3161 timestamps the app binary plus both installers, verifies the signatures, writes `SHA256SUMS.txt`, and **aborts if the published `AgentX.Core.dll` lacks the security types** — the exact regression that shipped in the public v2.1.1 asset (built from stale source). See [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).
- **Keyless supply-chain provenance in CI (cosign + Rekor).** New `.github/workflows/release-provenance.yml` signs the release `SHA256SUMS.txt` with [Sigstore](https://www.sigstore.dev/) `cosign` using GitHub's ambient OIDC identity — **no secret, no long-lived key** — records it in the public **Rekor** transparency log, and attaches the signature + ephemeral certificate to the release. Signing the manifest transitively covers both the SLIM (GitHub) and OFFLINE (R2) installers. This is the second of a two-layer model (local Authenticode + CI keyless provenance); end users can verify origin with `cosign verify-blob` — see [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md#verifying-a-download).
- **Free OSS code-signing path documented.** [`docs/SIGNPATH-APPLICATION.md`](docs/SIGNPATH-APPLICATION.md) is the application + canonical record for free Authenticode signing via the [SignPath Foundation](https://signpath.org/), removing the need for a paid certificate to clear the Windows SmartScreen "Unknown Publisher" warning.
- **CI vulnerability + quality gates (AX-QA-006, AX-QA-009, AX-QA-012, AX-QA-016).** Browser-extension and NuGet vulnerability gating; `AgentX.Core` coverage floors; a repository format gate; and removal of the unused React ESLint plugins that were the sole importers of the `@babel/core` dev advisory (`npm audit --omit=dev`: 0 vulnerabilities).
- **Android build CI (AX-QA-004).** New `.github/workflows/android-build.yml` compiles `src/AgentX.Mobile` (`net8.0-android`) on every change under it; iOS is conditioned out on Linux and deferred (needs a macOS runner).
- **Test isolation (AX-QA-011).** Workflow tests no longer write into the real user profile; removed 61 leaked `WorkflowResults` profile stub files.
- **GitHub Actions runtime** bumped off the deprecated Node 20 runtime.
- `Directory.Build.props` `<Version>` bumped `2.1.1` → `2.1.2` (single source; `AppVersionInfo` flows it to every surface).

---

## [2.1.1] — 2026-05-31 — "Bedrock" fresh-install fix

Patch release. Fixes a **critical fresh-install defect** found during full installer validation: on a brand-new machine the database came up empty (no tables) and every feature failed with `no such table: documents/memories/user_settings/...`.

### Fixed

- **Fresh installs now build the full database schema.** At startup `EnsureKeyApplied()` opens the SQLite connection (to apply the SQLCipher PRAGMA) before the migration runner, which creates an empty `agentx.db` file. The runner then saw `CanConnectAsync() == true`, mistook the empty file for a pre-migration install, ran baseline adoption — which *stamps* the baseline as applied **without creating tables** — and `MigrateAsync` skipped schema creation. Baseline adoption is now gated on the database actually containing application tables, so an empty database flows through `MigrateAsync` and receives the full schema. Verified end-to-end via a clean install → launch → uninstall: all 11 migrations apply, 40 tables created, zero `no such table` errors.
- Added a `MigrationRunner` regression test that opens the connection before running the runner, reproducing the real startup sequence (the prior fresh-DB test never did, which is why the defect slipped through).
- Zeroed out all 13 Release build analyzer warnings at the root (nullable annotations, an unused `async`, and test-only Moq/null-handling) — the build is now warning-free.

### Changed

- `Directory.Build.props` `<Version>` bumped `2.1.0` → `2.1.1`.
- Installer `AgentX-Setup.iss` `MinVersion` raised `10.0.18362` → `10.0.19041` to match the app's `TargetPlatformMinVersion` (older builds would install but fail to launch).

---

## [2.1.0] — 2026-05-30 — "Bedrock"

Final v2.1.0 release. Promotes the `2.1.0-preview.1` data-layer slice to a stable release and completes the v2.1 scope. Full notes: [`docs/v2.1.0-RELEASE-NOTES.md`](docs/v2.1.0-RELEASE-NOTES.md).

### Added

- **A1 Multi-Language UI** — six shipping locales (de / en-US / es / fr / ja / zh-CN) with CLDR pluralization, RTL-ready `FlowDirection`, a `LocaleAudit.Tool` CI gate (≥98% coverage), and per-page snapshot tests
- **A2 Keyboard-First Power Mode** — fuzzy Command Palette, Jump-To navigation, and a page-scoped shortcut Cheatsheet
- **B9 EF Core migrations** and **C13 SQLCipher at-rest encryption** promoted from preview to stable

### Changed

- **In-app User Guide localization completed** — every `UserGuide_*` string is now natively translated across all five non-English locales (de / es / fr / ja / zh-CN), replacing the prior English placeholders; stale placeholder headers removed
- `Directory.Build.props` `<Version>` bumped `2.1.0-preview.1` → `2.1.0`

### Fixed

- Documented previously-silent `JsonException` handling in HTML/PDF citation export
- Corrected stale calendar-sync and Serper knowledge-graph comments; removed dead internal-path references from release notes

### Rescoped

- **C14 Audit Log** remains targeted at **v2.1.5** — a Phase 2 Memory prerequisite that ships before Memory regardless of v2.1.5 timing

---

## [2.1.0-preview.1] — 2026-04-17 — "Bedrock" data-layer hardening

Pre-release shipping the data-layer slice of the v2.1 Bedrock hardening stream. Ships on `phase1-bedrock` at commit `e4bb5ce`. Full release notes: [`docs/v2.1.0-preview.1-RELEASE-NOTES.md`](docs/v2.1.0-preview.1-RELEASE-NOTES.md).

### Added

- **B9 EF Core migration runner** — `IMigrationRunner` + `MigrationRunner` with pending-migration API, `MigrationResult`, `PendingMigrationsException`, `AgentXDbContextFactory` for design-time tooling, `InitialBaseline` migration capturing current schema, and baseline-adoption for pre-migration installs
- **C13 SQLCipher at-rest encryption** — `SQLitePCLRaw.bundle_e_sqlcipher` provider, `IDatabaseKeyService` with DPAPI-wrap and UserPassphrase (PBKDF2-HMAC-SHA256, 600k iterations) modes, `IEncryptedConnectionFactory` applying `PRAGMA key` on every `SqliteConnection`, `IDatabaseEncryptionMigrator` using `sqlcipher_export` for atomic plaintext→encrypted conversion with rollback
- **C13 Settings UI** for the database encryption enable flow (`src/AgentX.App/Views/SettingsPage.xaml:525`). *Corrected 2026-09-06: this line originally read "tier-aware: Ultimate passphrase dialog, others transparent enable". There is no product-tier concept in this codebase — `grep -rn "Ultimate" src/` returns nothing, and there is no license or tier service. The real choice is a `KeyStorageMode` on the key service, `DpapiWrapped` or `UserPassphrase` (`src/AgentX.Core/Services/Security/DatabaseKeyService.cs:36`), which is a storage mode, not a paid tier.*
- **C13 Startup unlock flow** using `IEncryptionStateFile` marker to break the unlock ↔ migration chicken-and-egg
- **`InvalidDatabaseKeyException`** with SQLite ErrorCode-26 / "file is not a database" detection for wrong-passphrase recovery loops
- **Out-of-DB key storage** at `%LocalAppData%\AgentX\encryption.info.json` — separates encryption state from the encrypted vault so startup unlock has no DB dependency (C13 hotfix, merged 2026-04-17)

### Changed

- `EnsureCreatedAsync` and the manual `ALTER TABLE inbox_items` block are **removed** from app startup and replaced by `IMigrationRunner.RunAsync()` — all schema changes now flow through EF migrations
- All 5 production `SqliteConnection` creation sites route through `IEncryptedConnectionFactory` for uniform key application
- DI registrations for `DatabaseKeyProvider`, `EncryptedConnectionFactory`, and `DatabaseKeyService` are singletons (data-plane crypto lifetime invariant)
- `Directory.Build.props` `<Version>` bumped `2.0.0` → `2.1.0-preview.1`; added `AssemblyVersion`, `FileVersion`, and `InformationalVersion`

### Rescoped

- **C14 Audit Log** was originally targeted at v2.1.0 per the magnum-opus Bedrock scope. Rescoped to **v2.1.5** on 2026-04-17 to let the C13 encryption landing settle before layering an HMAC-chained audit subsystem on top. It remains a Phase 2 Memory prerequisite and will ship before Memory regardless of v2.1.5 timing.

### Security

- SQLCipher 4 AES-256-CBC at-rest encryption (opt-in, tier-aware)
- DPAPI-wrapped key material never written to the encrypted DB
- PBKDF2-HMAC-SHA256 (600,000 iterations) for passphrase mode, exceeding OWASP 2023 recommendation (600k)
- `.plain.bak` retained only through the atomic-swap critical section during encryption enable

### Known Issues

- Test-host shutdown hang (`H1`) — native library handles from SQLitePCLRaw / Whisper.Net / LLamaSharp prevent clean xUnit host exit. Tests pass in ~6s; CI uses `blame-hang-timeout=30s`.
- `dotnet ef migrations add` workflow requires `-p:CopyLocalLockFileAssemblies=true` pre-build followed by `--no-build` invocation (net8.0-windows TFM runtime-pack limitation).

---

## [2.0.0] — 2026-04-16 — "Enrichment" calendar/email/ide-awareness

Shipped to `main` via PR #1 (merge commit `99bf449`) on branch `feature/calendar-email-integration`.

### Added

- **Feature 9 HNSW ANN vector scaling** — Hierarchical Navigable Small World index for the vector store, with core, factory, and test coverage
- **Feature 10 Calendar + Email integration** — OAuth2 + PKCE + CSRF state infrastructure (`IOAuthService`), Google Calendar + Outlook Calendar providers, Gmail + Outlook Email providers, `CalendarPlugin` + `EmailPlugin` with `IPlugin` lifecycle, sync services with delta tokens, Settings UI with connect/disconnect, full search pipeline integration
- **Feature 12 Screen-awareness with IDE detection** — `IdeWindowDetector`, `ScreenContextResult.IdeContext`, Quick Chat prompt integration
- **`PluginType.DataConnector`** and scoped `IPluginContext` for plugin OAuth access
- OAuth settings, Calendar settings, Email settings in `AppSettings` + `OAuthProviderRegistry`

### Fixed

- OAuth `TokenResponse` deserialization (`[JsonPropertyName]` for snake_case OAuth2 fields)
- `OAuthService.BuildAuthorizationUrl` query string (manual URL escape via `List<KeyValuePair>` + `Uri.EscapeDataString` instead of `Dictionary.ToString()`)
- OAuth hardening — CSRF state parameter, PKCE code challenge, 5-minute HttpListener timeout, `IDisposable` lifecycle
- Cross-arch build — `RuntimeIdentifier` now derives from `Platform` so x86/x64/ARM64 builds work

### Changed

- Bumped `Directory.Build.props` `<Version>` to `2.0.0` (commit `2a4d5cc`)

---

## [1.5.0] — 2026-04-14 — "Expansion"

See [`docs/v1.5.0-RELEASE-NOTES.md`](docs/v1.5.0-RELEASE-NOTES.md).

Added: Web Content Ingestion Depth, Conversation Branching, Export Format Expansion, Deep Research Mode.

## [1.4.0] — 2026-04-14 — "Foundation + First Expansion"

See [`docs/v1.4.0-RELEASE-NOTES.md`](docs/v1.4.0-RELEASE-NOTES.md).

Added: DPAPI API Key Encryption, System Tray + Global Hotkey, Browser Extension, Multi-Model Routing.

## [1.3.0] — 2026-04-12

See [`docs/v1.3.0-RELEASE-NOTES.md`](docs/v1.3.0-RELEASE-NOTES.md).

Added: Workspace Profiles, Smart Inbox, Comparative Analysis, Voice Input, Plugin API, Collaborative Sync.

---

[2.1.2]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.2
[2.1.1]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1
[2.1.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.0
[2.1.0-preview.1]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.0-preview.1
[2.0.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.0.0
[1.5.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.5.0
[1.4.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.4.0
[1.3.0]: https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v1.3.0
