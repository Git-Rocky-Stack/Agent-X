# Chat Context Story Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for unified context-story surfacing

---

## Objective

Make the chat-side intelligence surface easier to understand by turning the latest assembled context state into one compact, human-readable story.

This slice does not add new intelligence or persistence. It only changes how the already-captured in-memory inspection snapshot is explained to the user.

---

## Approved Scope

### User experience shape

- add one derived `Context Story` sentence
- add 2-4 compact source chips that summarize the main context ingredients
- show the same story in:
  - the visible intelligence strip
  - a new top `Context Story` card inside the inspector

### Explicitly out of scope

- new persistence
- per-message inline explanation cards
- new analytics-like charts or metrics
- summary-history replay
- a second explanation engine outside the existing inspection snapshot

---

## Chosen UX Direction

### Approved approach

Use a shared, deterministic `Context Story` sentence plus source chips derived from the existing in-memory inspection snapshot.

### Why this was chosen

- it connects summary freshness, recall usage, compression, and fallback state into one understandable explanation
- it keeps the strip and inspector aligned instead of inventing two different stories
- it improves legibility without turning chat into a debugging surface
- it preserves the existing inspector metrics for users who want deeper detail

---

## Story Rules

### Primary rule

The story is derived from the latest captured inspection snapshot only.

### Normal assembled path

The sentence leads with durable summary state first, then adds recall and compression when present.

Examples:

- `Using a current durable summary and 1 recalled message from another conversation.`
- `Using a stale durable summary with 3 newer messages still outside it.`
- `Using a current durable summary, 2 recalled messages from other conversations, and compressed overflow context.`

### Fallback-heavy or reduced paths

If the response used limited visibility or a fallback-heavy path, that overrides the normal story so the UI does not imply more certainty than exists.

Examples:

- `This response used a limited-visibility path, so only partial chat context details are available.`
- `This response used the legacy context path, so the assembled context story is only partially inspectable.`
- `Agent-X selected thread context with lexical fallback and then layered in the durable context that fit.`

### Refresh failure behavior

- operational refresh state remains in the existing status surface
- the context story continues to describe the last usable snapshot
- refresh failures do not create a separate story mode

---

## Source Chips

The chips are factual labels, not decorative badges.

### Approved chip set

- `Current Summary`
- `Stale Summary`
- `1 Recall Match` / `N Recall Matches`
- `Compressed Overflow`
- `Lexical Fallback`
- `Legacy Fallback`
- `Limited Visibility`
- `Summary Only` when the chat surface only has a refreshed summary and not a newly assembled response context

### Chip rules

- only show chips for active ingredients or path-defining constraints
- do not show chips for absent features unless the absence explains the response path
- cap the list to the small derived set from the snapshot

---

## UI Placement

### Visible strip

- keep the existing freshness badge
- keep the current status line and actions
- add one short context-story line under the status text
- add the source chips under the story line when available

### Inspector

- add a new top `Context Story` card above the existing overview/summary/recall cards
- repeat the same story sentence from the strip
- show the same chips there before the lower-level metrics

This keeps the story visible in everyday chat while still giving the inspector a clean high-level entry point.

---

## Data Flow

### Approved direction

Keep the phrasing logic in the chat-side inspection model so both UI surfaces read from one source of truth.

### Flow

1. `ChatService` continues capturing the latest `ChatContextInspectionSnapshot`.
2. The snapshot exposes derived story text and derived story chips from its existing summary, recall, diagnostics, and limited-visibility fields.
3. `ChatViewModel` maps those derived values into strip/inspector-friendly bindable properties.
4. `ChatPage.xaml` renders them in the strip and inspector card.

No new database entity, migration, or background process is added.

---

## Failure And Empty States

- no active conversation: hide the strip entirely
- active conversation with no snapshot yet: show a neutral no-context story state
- limited-visibility or summary-only refresh paths: show the reduced-path story rather than pretending full assembly data exists
- story rendering must never block or affect message generation

---

## Verification Scope

- focused chat-service coverage for derived story generation on a normal assembled path
- focused chat view-model coverage for:
  - current story
  - stale story
  - limited-visibility story
  - no-snapshot conversation state
- WinUI build with `RuntimeIdentifier=win-x64`

No migration, analytics, or persistence testing is required for this slice.
