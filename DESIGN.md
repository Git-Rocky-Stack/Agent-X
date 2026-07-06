# Design System - Agent-X

**Aesthetic direction:** Command Console (Carbon Pro chassis, bench-instrument faceplate)
**Created:** 2026-07-05 via `/design-consultation` (ported and adapted from `Team-X\DESIGN.md`, Rocky's direction)
**Status:** Canonical. Governs all UI work in this repo. `AgentX.App` (WinUI 3) is the primary surface; `AgentX.Mobile` adopts tokens and vocabulary where MAUI allows.
**Family DNA:** Shares the Carbon Pro chassis language with Team-X (`Team-X\DESIGN.md`) and Vision Studio X (`Vision-Studio-X-website\DESIGN.md`): four-layer raised-hardware depth, machined radii, mechanical motion envelopes, hex socket bolts, brushed-aluminum stripes, anti-slop discipline.
**Adopts from Team-X (Rocky's 2026-07-05 call, D1/D2):** armed red `#AA2024` and the dual-form red rule; the four-face typography stack (Archivo / Public Sans / Departure Mono / Iosevka).
**Diverges on:** platform (native WinUI 3 XAML, not CSS - every recipe here is a XAML translation), theme set (Night Ops + Day Shift + a system-bound HighContrast third theme), annunciator vocabulary (document-intelligence lamps, not org-ops lamps), and instrument density (meters only where Agent-X has live data to bind).

> **The family story:** Strategia products share one chassis language, like Pioneer's professional division. Vision Studio is the CDJ (creative deck). Team-X is the DJM (command desk). **Agent-X is the rack unit - the archive deck: the instrument you feed your documents into, and it answers.** Same carbon, same bolts, same depth physics, same lamp discipline; its faceplate identity is the operator's private intelligence bench.

---

## Product Context

- **What this is:** Agent-X - local-first AI document intelligence for Windows. A native .NET 8 / WinUI 3 desktop app that turns a personal document collection into a queryable, AI-augmented knowledge base: Knowledge Vault ingestion, hybrid RAG retrieval (HNSW + FTS5 + RRF), streaming chat against local GGUF models, Smart Inbox triage, knowledge graph, analytics, workflows.
- **Who it's for:** A single operator working their own archive - researcher, builder, analyst. Closer to an engineer at a bench instrument than a consumer in a notes app.
- **Space/industry:** Local/private AI knowledge tools. Peers: LM Studio, Obsidian + AI plugins, AnythingLLM, Rewind-class recall tools. The category converges on either the lavender "AI uniform" or default-Fluent minimalism. Nobody ships instrument-grade hardware.
- **Project type:** Native Windows desktop (WinUI 3, MVVM, CommunityToolkit.Mvvm), MSIX-free Inno Setup distribution, plus a MAUI Android companion.

**Memorable thing (every decision serves this):** *"A precision instrument that knows everything you have ever filed - and nothing leaves the machine."*

**The inherited eureka:** dev-tool brands avoid red because in dev-tool semantics red = failure. Agent-X is not a dev tool - it is an instrument, and in instrument culture (broadcast master control, trading terminals, mission control) red is the authority palette. Strategia red `#AA2024` is a structural differentiation asset in a category converging on lavender and stock Fluent. **Red means LIVE, not error.** In Agent-X the canonical LIVE state is generation: the `GEN` lamp burns steady armed red while a model is producing tokens.

---

## Aesthetic Direction - Command Console, bench-instrument faceplate

**Visual thesis:** Pioneer-grade engineering artistry applied to personal intelligence: brushed black aluminum chassis, purpose-built LED placement, phosphor LCD wells for live telemetry, machined caps and hex socket bolts. Equipment that feels procured, not downloaded. The operator's own bench: dense, truthful, entirely offline.

**Decoration level:** Brutalist-maximalist within hardware discipline. Every section is a faceplate; every faceplate is milled aluminum on the carbon chassis. Nothing is purely decorative - every LED earns its placement (see the data-bound rule under Components).

**Mood:** The app is operated, not used. The first viewport is a poster, and the poster is your archive working: vault size, index health, model state, live token flow.

**Reference set (vocabulary, not literal copies):** Pioneer DJM/CDJ chassis language; Apollo-era flight consoles and broadcast master control (annunciator panels, stencil status words, master-caution acknowledge); Universal Audio Apollo (silver-hardware Day Shift reference); Bloomberg Terminal (data density as authority).

**Three deliberate departures from category norms:**
1. **Red = LIVE, not error.** Steady `#AA2024` means the machine is working for you: model generating, ingestion running hot. Faults are distinguished by form, not hue: warnings BLINK at 1Hz until acknowledged; live states burn steady; terminal faults hold steady on the dedicated NO-GO tone.
2. **Stencil words, not icons, for status.** All state renders as 2-5 letter codes in Archivo caps on lamp tiles: `GO` `HOLD` `NO-GO` `STBY` `GEN` `LOCAL`. Icons survive for navigation only. Every screenshot markets itself.
3. **Functional hardware density.** LCD wells and segment meters wherever Agent-X has live data - tokens/sec, VRAM, embedding queue depth, vault counts - all data-bound. The category does minimal-with-one-accent; Agent-X does instrument-grade density where every element works.

**Signature element - the Instrument Strip + Annunciator Cluster:**
- **Instrument Strip** (bottom status bar, reborn): a row of recessed LCD wells with phosphor Departure Mono readouts - `MDL` (loaded model), `TOK/S`, `VRAM`, `VAULT` (doc count), `IDX` (embedding queue) - plus the `LOCAL` lamp. Fed by the existing `StatusBarService` and `IPrivacyStatusService`; every value is real.
- **Annunciator Cluster** (instrument strip, right side - lamps must be clickable for the ack ritual and teleports, and the WinUI caption row is non-client drag region, so Agent-X's annunciators live on the bottom strip, not the title bar): compact lamp tiles per subsystem: `MDL` `LOCAL`/`NET` `INBOX` `SYNC` `JOBS` `BAK` all shipped (the last four are fed by `AnnunciatorService`, a two-cadence typed poller: inbox count and sync posture every cycle, backup age and workflow-run health every 4th). `IDX` reads as an LCD queue-depth readout rather than a lamp.
  - **The strike:** when a lamp goes live it IGNITES - 80ms attack with overshoot bloom, settles steady (the LED-ramp envelope). Reused everywhere a state goes live.
  - **Master-caution acknowledge:** a blinking warning lamp blinks until clicked, then stays lit until resolved. Acknowledgment is a physical ritual, not a dismissed toast.
  - **Lamps are teleports:** clicking a lit lamp navigates to the source page (`MDL` to Model Manager, `IDX`/`JOBS` to Operations, `INBOX` to Inbox, `SYNC` to Sync Settings, `BAK` to Backup and Restore, `NET` to privacy settings).
- **The privacy lamp:** `LOCAL` burns steady green when zero cloud providers are active (the state-aware no-cloud claim); it swaps to a steady amber `NET` when a cloud model or web search is opted in. The privacy posture is always one glance away - this is Agent-X's ON AIR light inverted.

---

## Four-Layer Raised-Hardware Depth System (WinUI translation)

Inherited structurally from the family. **Depth comes from layered gradients, 1px edge-light strips, and inner bevel borders - never flat drop shadows alone.** Every surface declares its layer; mixing raised and recessed treatments on one element is forbidden.

| Layer | What it is | Agent-X examples |
|-------|------------|------------------|
| **0 - Chassis** | Window/page background, flat | `WindowBackgroundBrush` canvas |
| **1 - Raised faceplate** | Major section panels with stripe + hex bolts | Settings sections, dashboard panels, dialogs |
| **2 - Recessed well** | Carved into the faceplate; **always dark in Night and Day** | Chat stream viewport, code blocks, LCD readouts, form inputs, search boxes, list wells |
| **3 - Raised control** | Sits on faceplate or well | Buttons, lamp tiles, document cards, chips, toggle caps |

### XAML recipes (canonical, Night Ops values)

CSS box-shadow stacks do not exist in XAML. The equivalent construction, codified as reusable styles in `Styles/Hardware.xaml`:

- **Faceplate (Layer 1):** a `Grid`/`Border` styled with a vertical `LinearGradientBrush` (`#1C1C1C` at 0, `#151515` at 0.35, `#101010` at 0.7, `#0C0C0C` at 1), `BorderBrush` white at 6% opacity, `CornerRadius` 2. Bevel: an inner 1px top highlight `Border` (white 10%) and 1px bottom shade (black 70%). Ambient depth: a Composition `DropShadow` (via `AttachedShadow`/`ThemeShadow`-equivalent helper) with large soft radius, black at 55%.
- **Recessed well (Layer 2):** `Border` with near-void vertical gradient (`#080808` to `#0A0A0A`), `BorderBrush` black 70%, `CornerRadius` 2, and an inner top shade strip (black 80%) to read as carved. No outer shadow ever.
- **Raised control (Layer 3):** `Border`/`Button` chrome with vertical gradient `#1F1F1F` to `#121212`, top highlight 1px white 10%, `CornerRadius` 4, small tight drop shadow.
- **Brushed stripe header (36px, top of every faceplate):** horizontal repeating grain is DIRECTIONAL machine brushing - implemented as a thin horizontal `LinearGradientBrush` tile or a pre-rendered 3px grain asset stretched horizontally, over a `#2A2A2A` to `#181818` vertical base; 1px black bottom rule; 44px side padding to clear corner bolts. Carries a Departure Mono kicker `MOD - NAME - NN` and optional lamp.
- **Hex socket cap bolts:** 20px reusable `HexBolt` control (concentric: radial-gradient outer cap, hexagon `Polygon` socket, countersunk halo ring), 4 per faceplate, inset 10px from corners.

### Day Shift recipes (silver anodized)

Same geometry and construction; surfaces re-skin to brushed natural aluminum: faceplate gradient `#F5F5F3` to `#D8D8D4`, borders black 12%, top highlight white 92%; stripe `#F0F0ED` to `#D5D5D1` with dark grain lines; controls `#F7F7F5` to `#DEDEDA`; bolts silver radial with dark socket. The chassis behind the plates stays darker than the plates (`#C4C4BF` range) so raised silver reads as raised.

### The displays-stay-dark rule (canonical, Night and Day)

**LCD wells, the chat token-stream viewport, code blocks, segment-meter windows, lamp-tile caps, and form input wells remain void-black in BOTH shifts** - exactly like silver studio hardware keeps black displays and black buttons. Recessed wells (Layer 2) never invert. This keeps phosphor glow and lamp legibility identical across shifts and is what makes Day Shift read as real silver gear rather than an auto-inverted theme.

**HighContrast is exempt from the entire hardware skin.** See Theme Policy.

---

## Typography - four faces, four jobs, zero overlap

All fonts SIL OFL. **Bundled as static TTF assets under `Assets/Fonts/` and referenced with `ms-appx:///Assets/Fonts/<file>.ttf#<Family>`** - local-first product; no runtime font downloads, no reliance on fonts being installed. Segoe UI Variable and Cascadia Code are superseded during the sweep.

| Role | Family | Spec | Use |
|------|--------|------|-----|
| **Display / Placards** | **Archivo** (expanded static instances) | weight 600-800, ALL-CAPS, `CharacterSpacing` 50-100 (WinUI units, = 0.05-0.1em) | Page titles, section placards, lamp codes, nav stencils. Stenciled equipment lettering. |
| **Body / UI** | **Public Sans** | 400-700, 13-14px UI, line height 1.5-1.6 | All prose, UI copy, form values. Government-grade workhorse. |
| **Telemetry** | **Departure Mono** | 10-12px (pixel grid: use exact sizes, no fractional scaling of the face) | **Any number that updates live:** token counts, TOK/S, VRAM, timestamps, doc counts, LCD content, kbd hints. |
| **Code / Streams** | **Iosevka Term** | 10-13px | LLM token streams, code blocks, logs, file paths. Density is a feature. |

**Rules:**
- If a number updates live, it wears Departure Mono inside an LCD well. If a human reads it as prose, it is Public Sans. Display sizes are always Archivo caps.
- **NEVER use** Inter, Roboto, Arial, Helvetica, Open Sans, Lato, Montserrat, Poppins, Space Grotesk, system-ui, or Segoe as primary faces on swept screens. (Unswept screens keep Segoe until swept; never mix stacks on one swept screen.)
- Migration note: Cascadia Code (current incumbent) is superseded by Iosevka Term (streams/code) + Departure Mono (telemetry) during the sweep.
- WinUI notes: prefer static instances over the variable TTFs (DirectWrite named-instance resolution through XAML `FontFamily` is unreliable); `CharacterSpacing` is in 1/1000 em; set `FontFamily` tokens once in `Typography.xaml` and consume via `StaticResource`.

**Scale:** display 28-48px (Archivo caps), section titles 20-24px (Archivo caps), headings via Public Sans 600 at 24/20/16, body 14, body-small 12, caption/placard 10.5-11 (Archivo 700 caps or Departure Mono UC), telemetry 10-26 (Departure Mono), streams/code 12-13 (Iosevka Term).

---

## Color

**Chromatic energy lives entirely in the LEDs; the chassis stays neutral so the armed red commands the bench.**

### Carbon ramp (Night Ops surfaces - family chassis)

| Token | Hex | Use |
|-------|-----|-----|
| `Void` | `#000000` | LCD wells, deepest recess (both shifts) |
| `Carbon950` | `#050505` | Chassis / page canvas |
| `Carbon900` | `#0D0D0D` | View interiors, sidebar floor |
| `Carbon850 / 800 / 750` | `#101010 / #141414 / #1A1A1A` | Panel bases, elevated surfaces |
| `Carbon700` | `#262626` | Dividers |
| faceplate gradient | `#1C1C1C` to `#0C0C0C` | Brushed black aluminum (Layer 1) |

### Text - silver ramp

| Token | Night | Day | Use |
|-------|-------|-----|-----|
| `Platinum` | `#F5F5F5` | `#1A1A1A` | Primary text (engraved enamel) |
| `SilverBright` | `#D1D1D1` | `#2E2E2C` | Emphasis |
| `Silver` | `#B3B3B3` | `#44443F` | Secondary text |
| `SilverMute` | `#888888` | `#62625E` | Tertiary, silkscreen labels |
| `Graphite` | `#5A5A5A` | `#96968F` | Disabled, unlit lamp text |

### Agent-X brand - ARMED RED (the identity)

| Token | Hex | Use |
|-------|-----|-----|
| `Armed` | `#AA2024` | Strategia red. Machined-cap gradient `#C8333A` to `#AA2024` (55%) to `#7F171A` for consequential command buttons, active nav tile, GEN lamp. **Steady = LIVE / command authority.** |
| `ArmedLit` | `#E0252B` | Hot readouts, glow text, backlit accents |
| `ArmedDeep` | `#7F171A` | Cap borders, pressed states |
| `ArmedGlow / ArmedSoft` | `#38E0252B / #1FAA2024` | Glow rings, selection tints |

The legacy cardinal ramp (`Red500 #C41E3A` family) is REBUILT around armed: `Red300 #E0252B`, `Red400 #C8333A`, `Red500 #AA2024`, `Red600 #941B1F`, `Red700 #7F171A`, `Red800 #5E1114`, `Red900 #430C0E`, `Red950 #2C0709`; light tints `Red200 #F08A8E`, `Red100 #F7BDBF`, `Red50 #FBE7E8`. Token keys are preserved; only values change, so all 46 views re-arm via the token layer.

**Focus policy:** the keyboard-focus indicator is the armed glow ring (`ArmedGlow`, 2px) on flat and raised interactive surfaces - the brand asserting itself on interaction. One deliberate exception: machined caps and lamp tiles carry a 2px neutral outline (`Ring`: chrome `#E6E6E6` at Night, dark graphite in Day) because a neutral outline survives the cap's own gradient stack and contrasts every cap fill including armed and warn caps. Implemented via `FocusVisualPrimaryBrush` overrides in the relevant styles. HighContrast keeps system focus visuals untouched.

### LED semantics (lamp vocabulary - identical both shifts)

| Token | Hex | Meaning | Form |
|-------|-----|---------|------|
| `LedGo` | `#41E25E` | GO / running / healthy / LCD phosphor default | Steady |
| `LedHold` | `#FFB000` | HOLD / caution / pending / queued | Steady |
| `LedWarn` | `#FF4438` | Unacknowledged warning | **Blinking 1Hz only** - never steady |
| `LedNoGo` | `#C8453E` | NO-GO / terminal fault (failed import, failed run, dead model) | **Steady** (ignite once, then hold) |
| `LedScope` | `#58C4BC` | Informational / analysis in progress - the rarest color | Steady |
| `Chrome` | `#E6E6E6` | Polished bits: the rare chrome CTA cap + Night machined-cap focus outline. Family accent, used sparingly. | - |

Render LEDs with glow (Composition drop shadow or layered `TextBlock` shadow at `currentColor`, 8px class). Day Shift darkens LED TEXT colors where they sit on silver surfaces (`#177A3D` green, `#996300` amber, `#C81E13` red, `#256F69` cyan) - LED dots and anything inside dark wells keep night values. Implemented as two brush families: `Led*TextBrush` (theme-aware) and `Led*LampBrush` (shift-invariant).

**The dual-form red rule (non-negotiable):** steady red = LIVE/armed/command; blinking red = a question that demands an answer (click to acknowledge, then steady until resolved). Never use blink for anything else; never use a steady `LedWarn`. A failed thing that is not awaiting acknowledgment is NO-GO (steady `LedNoGo`), never a steady warn.

**Legacy status brush migration:** `SuccessBrush`/`OnlineBrush` map to `LedGo` family; `WarningBrush` to `LedHold`; `ErrorBrush` to `LedNoGo` (steady faults) or `LedWarn` (only in blink-until-ack contexts); `InfoBrush` to `LedScope`; `OfflineBrush` to `Graphite`. Legacy keys keep working (re-pointed values) until every consumer is swept.

### Hairlines

`Hairline`: white 8% (Night) / black 10% (Day); strong variants at 2x.

---

## Spacing

Base-4, Fibonacci-flavored console scale for swept screens (all stops are 4px multiples; compatible with the 8-point grid). Mechanical even spacing is itself an AI-slop signal; broken cadence reads as composed.

```
Sp1: 4    (micro)      Sp2: 8    (compact)
Sp3: 12   (default)    Sp4: 20   (comfortable)
Sp5: 32   (panel)      Sp6: 52   (section)
Sp7: 84   (canvas)
```

**Density: professional compact.** Density is respect for the operator. Legacy `Spacing*` tokens (2-64) remain valid on unswept screens and retire during the sweep.

## Border Radius - machined

```
RCard:    2     (faceplates, panels, wells - machined plate)
RControl: 4     (buttons, lamps, inputs, cards - mechanical cap)
ROverlay: 8     (dialogs/flyouts - the only soft surface)
RPill:    9999  (LED dots, avatars - true circles only)
```

NEVER uniform radius across surface types - the varied hierarchy is itself anti-slop signal. Legacy `RadiusLG`/`RadiusXL` (12/16) retire to `ROverlay` during the sweep.

---

## Layout - the bench composition (WinUI shell mapping)

The first viewport is a poster, and the poster is your archive operating. No welcome copy.

- **Title bar (top, edge-to-edge):** wordmark + active context, Annunciator Cluster (right), Ctrl+K command palette hint. Maps to the existing `AppTitleBar`.
- **Left nav rail:** the existing `NavigationView` pane restyled: group headers (`INTELLIGENCE` `KNOWLEDGE` `TRIAGE` `SYSTEM`) become Archivo stencil placards; active item = armed-red bordered tile; icons survive here (navigation only).
- **Center stage:** the selected page. Dashboard is the poster view: vault, index, model, and activity as faceplates with live wells.
- **Instrument Strip (bottom status bar):** recessed LCD wells - `MDL`, `TOK/S`, `VRAM`, `VAULT`, `IDX` - plus the `LOCAL`/`NET` privacy lamp. Fed by `StatusBarService` and `IPrivacyStatusService`.
- **Command palette (Ctrl+K):** an overlay faceplate (`ROverlay`), search input as a recessed well.

**Faceplate composition (every major section):** raised faceplate, 4 corner hex bolts, brushed stripe header with Departure Mono kicker `MOD - NAME - NN` and optional lamp, body, recessed wells for data, raised controls on wells.

**Live state:** every page keeps at least one always-truthful live element (blinking cursor in the stream well, pulsing GEN lamp during generation, IDX meter while embedding). Simulated liveliness is banned; if nothing is live, nothing animates.

**Adaptive breakpoints:** unchanged (960 / 1280 / 1600, `BreakpointMedium/Wide/XWide`).

---

## Component Vocabulary

| Primitive | Spec | Supersedes (legacy style keys) |
|---|---|---|
| **Lamp tile** | Raised control cap + Archivo stencil word (`GO/HOLD/NO-GO/STBY/GEN/LOCAL`), LED-colored text + tint + glow. 26px standard / 19px small. Always dark-capped (both shifts). | `BadgeDefaultStyle`, `BadgeAccentStyle`, `BadgeSuccessStyle`, `BadgeWarningStyle` |
| **Annunciator cluster** | Lamp strip in the title bar; strike ignition; 1Hz blink until click-to-ack; lit lamps teleport to source page. | parts of `NotificationOverlay` |
| **Segment meter** | Segment cascade (green to amber to red zones), horizontal or vertical, **data-bound only** (TOK/S, VRAM, IDX queue), flickering tip on the boundary segment, IEC-style ballistics. Unlit segments stay dark in both shifts. | `AccentProgressBarStyle` (where the value is live) |
| **LCD well** | Recessed void-black window + Departure Mono phosphor text with glow. Green default; amber/red variants for caution/hot values. | metric readouts in status bar and dashboards |
| **Machined caps (buttons)** | Raised control + specular top highlight. Armed-red cap = consequential commands only (Generate, Import, Delete-with-consequence). Chrome cap = the single polished CTA per view at most. Press: 1px cap travel, 80ms. | `AccentButtonStyle`, `SecondaryButtonStyle`, `GhostButtonStyle`, `IconButtonStyle` |
| **Console plate forms** | Labels in Departure Mono UC; inputs/selects as recessed dark wells (light text, both shifts); error = `LedWarn` border + NO-GO hint text. | `InputTextBoxStyle`, `ChatInputStyle`, `SearchInputStyle` |
| **Annunciator alerts** | Dark module rows (both shifts) with LED dot + Archivo title + Public Sans body; GO/HOLD/WARN(blink)/SCOPE variants. | `InlineErrorBarStyle`, `InlineSuccessBarStyle`, `InlineInfoBarStyle` |
| **Document card** | Raised control: Archivo title + lamp + type/tags + Departure Mono metadata (size, date, chunk count). | `CardStyle`, `CardElevatedStyle`, `CardInteractiveStyle` |
| **Stream viewport** | Recessed well + Iosevka Term token stream + phosphor cursor; TOK readout in the well's corner. | chat message area, code blocks |
| **Faceplate** | Layer 1 recipe + stripe + bolts. | `CardAccentStyle`, settings section containers |
| **Bat-lever switch** | Recessed track + machined cap thumb; armed-red when on. | `ToggleSwitch` restyle |

**Data-bound rule:** every LED, meter, and lamp is bound to a real value or a truthful live simulation of one. Decorative instruments are banned.

**Migration discipline:** until a screen is swept, existing shipped primitives remain in force. Never mix old and new families on one swept screen.

---

## Motion - mechanical envelopes (WinUI translation)

No floating blobs, no gradient drift, no decorative shimmer. Storyboards and Composition animations only, with these envelopes:

| Event | Behavior | Spec |
|---|---|---|
| **LED ramp ("the strike")** | State goes live: ignition with overshoot bloom, settles at 85% sustained | 80ms attack / 240ms decay; KeySpline `0.2,0.7 0.3,1` |
| **Warning blink** | 1Hz step until acknowledged; then steady until resolved | 1000ms discrete keyframes; ack = physical ritual |
| **Meter ballistics** | 300ms attack to indicated peak with analog overshoot, 300ms release | KeySpline `0.2,0.85 0.15,1` |
| **Button press** | 1px cap travel + brightness lift on hover | 80ms; KeySpline `0.32,0.72 0,1` |
| **Page transitions** | Functional cross-fade, no choreography | 240ms |
| **Reduced motion** | All animation suppressed; states legible by color + form | respect `UISettings.AnimationsEnabled`; collapse to 0ms |

---

## Theme Policy - dual-shift plus HighContrast

- **Night Ops** (Default/Dark, the signature): brushed black aluminum on AMOLED carbon.
- **Day Shift** (Light): silver anodized faceplates - a deliberately designed variant, never an auto-invert. Replaces the previous executive-white light palette.
- **Displays stay dark in both shifts** (canonical rule above).
- **HighContrast:** fully exempt from the hardware skin. Binds to `SystemColor*` tokens exactly as today - flat surfaces, full-opacity borders, system accent, system focus visuals. No gradients, no lamps-as-color (stencil words carry state on their own, which is why word-lamps beat icon-status for accessibility). This theme is untouchable by the sweep.
- Armed red, LED meanings, geometry, depth structure, spacing, type: identical across Night and Day.

---

## Anti-Slop Validation

Inherited family rules + Agent-X specifics. On every UI change, re-validate:

- NO Inter/Roboto/Arial/Helvetica/Open Sans/Lato/Montserrat/Poppins/Space Grotesk/system-ui/Segoe as primary fonts on swept screens
- NO purple-violet gradients; NO indigo `#6366F1`; NO lavender "AI uniform"
- NO 3-column icon-circle feature grids; NO centered-everything-uniform-spacing
- NO uniform border radius; NO flat drop-shadow elevation (declare a depth layer)
- NO fractal-noise/grain textures - brushed grain is directional or absent
- NO icons for status - status is a stencil word in a lamp tile
- NO steady red for errors / NO blinking red for live states (the dual-form rule)
- NO purely decorative LEDs, meters, or lamps - every element is data-bound
- NO auto-inverted light theme - Day Shift uses the designed silver recipes; displays stay dark
- NO hardware skin leaking into HighContrast - HC stays system-bound and flat
- NO prompt box as the primary visual metaphor - the archive is the center, chat is one instrument on the bench

---

## Migration Plan (approved 2026-07-05, D3: full port in tiers)

| Tier | Scope | Status |
|------|-------|--------|
| 0 | This DESIGN.md + repo CLAUDE.md pointer | DONE 2026-07-05 |
| 1 | Token layer: `Colors.xaml` (armed red, carbon ramp, LED semantics, Day Shift silver, HC untouched), `Typography.xaml` (four faces), font assets, `Styles/Hardware.xaml` primitives (faceplate, well, cap, lamp, stripe, bolt) | DONE 2026-07-05 (build 0/0, smoke PASS, 2775/2775 tests) |
| 2 | Anchor screens swept: MainWindow shell (nav rail, instrument strip, annunciator lamps), Dashboard (17 faceplates), Chat (GEN lamp, LCD token well, stream chrome), Settings (10 faceplates, bat-lever toggles); shared primitives (Faceplate control, LampTile control, machined cap buttons, well inputs, Fluent lightweight overrides) | DONE 2026-07-05 (build 0/0, 2775 + 32 locale tests, Night and Day screenshots) |
| 3 | Sweep of all remaining views; retire legacy style keys; remove Segoe/Cascadia fallbacks from swept paths; full annunciator cluster (INBOX/SYNC/JOBS/BAK lamps via an aggregation service); Analytics chart palette redesign to LED hues | DONE 2026-07-05 (123 faceplates across 19 more pages, 71 resw titles uppercased, off-palette hues purged, SegmentMeter on Dashboard + Hardware Advisor, AnnunciatorService + INBOX/SYNC/JOBS/BAK lamps with teleports; build 0/0, 2775 + 32 locale tests, 29/29-page UIA nav smoke in BOTH shifts, Night and Day captures) |

Checkpoint after each tier: x64 build green, UIA AutomationId smoke pass, screenshots for Rocky's review. The token layer alone re-arms the accent and re-skins Light across all 46 views; hardware depth arrives per-screen with the sweep.

---

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-07-05 | Command Console system ported from Team-X and adapted for WinUI 3 | Rocky's direction: implement the existing Team-X design for Agent-X. Adaptation, not copy: CSS recipes translated to XAML gradient/border/composition constructions; Electron dual-theme policy extended with a system-bound HighContrast third theme. |
| 2026-07-05 | D1: armed red `#AA2024` adopted; cardinal `#C41E3A` retired | Family alignment: one Strategia red across Team-X, Sys-Monitor, Agent-X. Cardinal ramp rebuilt around armed; token keys preserved so the re-arm is a value swap. |
| 2026-07-05 | D2: Team-X type stack adopted verbatim (Archivo / Public Sans / Departure Mono / Iosevka) | Typography is half the Command Console identity; stencil placards and phosphor telemetry do not read in Segoe. Static TTF instances bundled locally (local-first, no downloads). |
| 2026-07-05 | D3: full port executed in tiers with build + UIA checkpoints | Same phased pattern that shipped the Tier 5a/5b theming migration cleanly across 46 XAML files. |
| 2026-07-05 | Flat-elevation doctrine superseded by four-layer hardware depth | The previous `Colors.xaml` banned gradients as AI-slop; Command Console's gradients are machined material recipes, and its own anti-slop rules ban flat drop-shadow elevation. The hardware doctrine wins by Rocky's port decision. |
| 2026-07-05 | HighContrast exempt from the hardware skin | Accessibility is non-negotiable: HC keeps `SystemColor*` bindings, flat surfaces, and system focus visuals. Stencil word-lamps carry state without color. |
| 2026-07-05 | Agent-X identity: the archive deck / bench instrument; `GEN` lamp is the LIVE state; `LOCAL` lamp is the privacy annunciator | Family story slot next to Team-X (command desk) and Vision-X (creative deck). The privacy lamp makes the local-first claim a permanent, truthful instrument instead of marketing copy. |
| 2026-07-05 | Annunciator cluster placed on the instrument strip (bottom), not the title bar | Lamps must be clickable (ack ritual, teleports); the WinUI caption row registered via SetTitleBar is non-client drag region where XAML elements cannot receive input without per-rect passthrough plumbing. Truthful interaction beats literal placement. |
| 2026-07-05 | Theme persistence bug fixed during the Tier 2 sweep | ThemeService saved under key "app.theme" but SettingsService resolves keys by AppSettings property name via reflection, so the theme choice silently never persisted and every launch fell back to Dark. Added AppSettings.Theme ("Dark" default) and re-keyed ThemeService; Day Shift now survives restart (proven by the checkpoint capture launching Light from the saved setting). |
| 2026-07-05 | Inset content cards are shift-following surfaces, not wells (`CardInsetStyle` and inline equivalents ride `CardBrush`, never `InputBackgroundBrush`) | The displays-stay-dark rule covers LCD wells, lamp caps, and instrument readouts - not generic content cards. Tier 2's flip of `InputBackgroundBrush` to well-dark in both shifts silently dragged ~100 inset-card usages across 24 files dark, pairing Day-dark text with dark grounds. Day QA in the Tier 3 checkpoint caught it; content surfaces re-pointed to `CardBrush`, bar/progress tracks to `CardPressedBrush`, while true data readouts (plugin install path and settings JSON, user-guide terminal commands, cheatsheet key chords) were promoted to proper wells with `WellText*` foregrounds. |
| 2026-07-05 | Status tones are shift-aware (`StatusToColorConverter` resolves Night vs Day LED text ramps via the root's ActualTheme) | LED text tones are tuned for dark grounds; on Day Shift silver they fail contrast (e.g. LedGo `#41E25E` at ~1.9:1). The converter now returns the darkened `Led*Text` Day ramp (`#177A3D` `#996300` `#C81E13` `#256F69`) when the root element renders Light. Read from the root's ActualTheme because ThemeService applies themes per root element, which app-level resource lookups do not follow. |
| 2026-07-05 | Static status dots eliminated (Model Manager connection dot, Sync Settings STATE dot) | Both dots were one-shot brushes initialized to green and never updated - an instrument that lies. Model Manager now binds GO/HOLD to `ViewModel.IsConnected`; Sync Settings binds a typed `CurrentSyncState` enum (never the display string) through an LED-tone mapper mirroring the SYNC strip lamp semantics. |

---

**This document is the source of truth.** All Agent-X UI work anchors to the tokens, recipes, and rules defined here. Where Agent-X is silent on a pattern, fall back to `Team-X\DESIGN.md`, then `Vision-Studio-X-website\DESIGN.md` (the family chassis progenitor) - but Agent-X's divergences (WinUI translation, HighContrast exemption, document-intelligence lamp vocabulary) always win inside this repo. Until a screen is touched by the sweep, existing shipped primitives remain in force; never mix old and new families on one swept screen.
