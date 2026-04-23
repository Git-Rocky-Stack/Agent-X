# Chat Intelligence Strip Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for visible freshness-strip follow-on slice

---

## Objective

Make durable summary freshness visible in the everyday chat surface without requiring the user to open the deeper context inspector.

This slice is a narrow follow-on to the context inspector. It adds a lightweight, always-visible status strip under the top chat toolbar so users can immediately see whether conversation intelligence is current, stale, pending, or unavailable.

---

## Approved Scope

### User experience shape

- add a slim persistent strip directly under the top chat toolbar
- anchor the strip around a freshness badge
- keep the strip compact and read-only
- preserve the existing on-demand inspector as the deep explanation surface

### Explicitly out of scope for this slice

- full summary text in the strip
- recalled-message details in the strip
- token or compression metrics in the strip
- force-refresh actions
- per-message inline intelligence cards
- new persistence

---

## Chosen UX Direction

### Approved approach

Use a `summary-first` visible strip with:

- a freshness badge
- one short status line
- an `Inspect Context` action

### Why this was chosen

- it keeps intelligence state visible without expanding chat into a heavy admin surface
- it complements the inspector instead of duplicating it
- it makes summary freshness legible on every turn, not only after deliberate inspection
- it keeps the surface small enough to survive narrow widths

---

## Strip Placement And Layout

Place the strip directly under the top chat toolbar and above the message stream.

### Behavior

- hide the strip when there is no active conversation
- show the strip for an active conversation even if no captured context exists yet
- when no captured context exists, show a neutral empty-state message instead of blank chrome
- keep the strip visually closer to a system status banner than a message bubble

### Layout shape

- left: freshness badge
- center: one-line status text
- right: `Inspect Context` action

On narrower widths, the strip should stay one compact row as long as possible and allow the center text to trim before pushing content into a heavier multi-line layout.

---

## Badge States And Rules

The strip uses the same latest in-memory inspection snapshot as the inspector.

### `Current`

Use when:

- a latest inspection exists
- a durable summary exists
- that summary is not stale

Status text example:

- `Summary current • 2 key points available`

### `Stale`

Use when:

- a latest inspection exists
- a durable summary exists
- that summary is stale

Status text example:

- `Summary stale • 3 newer messages not folded in`

### `Pending`

Use when:

- a latest inspection exists
- the path was not limited-visibility
- summary details are temporarily unavailable even though context assembly was captured

Status text example:

- `Summary refresh pending`

### `Unavailable`

Use when:

- there is no latest captured inspection for the active conversation
- or the response used a limited-visibility path
- or durable summary data was unavailable for the response path

Status text examples:

- `No conversation context captured yet`
- `Summary unavailable for this response path`

---

## Interaction Model

- the badge is not clickable in this slice
- the strip exposes one explicit action: `Inspect Context`
- `Inspect Context` opens the existing context inspector
- no hover-only dependency for core information
- no mutation or force-refresh actions yet

---

## Transition Behavior

- selecting a conversation updates the strip immediately from the latest in-memory inspection snapshot
- during generation, the strip continues to show the previous completed state
- after generation completes, the strip swaps to the new snapshot
- limited-visibility responses move the strip into `Unavailable` while the inspector explains the reduced path
- a new conversation with no captured context shows the neutral unavailable/empty state, not hidden chrome

---

## Failure Handling

- the strip must never block chat rendering or generation
- missing summary inspection data degrades to `Pending` or `Unavailable`, not broken UI
- no active conversation hides the strip entirely
- strip state must be derived from the existing in-memory snapshot path, not from a second persistence seam

---

## Implementation Boundary

### Files

- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

### View-model work

- derive the strip state from the latest inspection snapshot already mapped for the inspector
- add compact UI properties for:
  - strip visibility
  - badge text
  - one-line status text
  - mutually exclusive badge-state booleans
- keep the strip state derived from the same inspection source of truth

### UI work

- add the strip under the top toolbar
- render state-specific badge surfaces
- keep the strip compact and visually system-like
- wire `Inspect Context` into the existing inspector toggle

---

## Verification Scope

- view-model tests for current, stale, unavailable, and hidden states
- WinUI build with `RuntimeIdentifier=win-x64`

No new service or migration tests are required unless the implementation expands beyond the approved boundary.
