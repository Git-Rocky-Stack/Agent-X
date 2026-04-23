# Chat Inline Context Inspect Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for clickable inline context-note inspection

---

## Objective

Let users open the full chat context inspector directly from an assistant message’s inline `Context Used` note.

This slice builds on the shipped session-only inline note behavior. It does not add new persistence or a second inspector surface.

---

## Approved Scope

### User experience shape

- make the entire inline `Context Used` note interactive
- click, tap, or keyboard-activate the note to open the existing inspector
- load the inspector with the selected assistant message’s associated snapshot
- keep the visible strip anchored to the latest conversation snapshot

### Explicitly out of scope

- a second dedicated inline panel
- per-message persistence across restart
- a new right rail or split inspector
- new analytics or provenance views

---

## Chosen UX Direction

### Approved approach

Use the whole inline note as a lightweight inspect affordance.

### Why this was chosen

- it keeps the message surface clean
- it makes the note feel attached to the answer rather than like a separate action row
- it reuses the existing inspector instead of inventing another drilldown surface

---

## Interaction Model

- the entire inline note is clickable and keyboard-focusable
- activation opens the existing context inspector
- the inspector shows the snapshot associated with that assistant message
- hover, focus, and pressed states should make the note read as interactive without becoming loud

### Important behavior rule

Selecting an older message note must not change the visible intelligence strip’s state.

The strip continues to describe the latest conversation snapshot. The inspector may temporarily inspect an older message snapshot.

---

## Data Flow

### Approved direction

Reuse the existing in-memory `MessageId -> ChatContextInspectionSnapshot` association.

### Flow

1. Each eligible assistant message already carries an inline note and has a session-only snapshot mapping.
2. Activating the note passes the `ChatMessageItem` to a view-model command.
3. The command looks up the stored snapshot by `MessageId`.
4. The command opens the existing inspector and applies that snapshot to the inspector display state only.
5. The latest conversation snapshot remains the strip source of truth.

---

## State Boundaries

### Latest strip state

- remains derived from the latest conversation snapshot
- does not change when the user inspects an older message

### Inspector state

- may show the latest snapshot by default
- may pivot to an older message snapshot when launched from an inline note
- resets back to the active conversation’s latest snapshot when appropriate entry points intentionally request latest context

---

## Failure Handling

- if a message has no stored session snapshot, the command becomes a no-op
- no note should render when no snapshot exists, so missing-snapshot activation should be rare and harmless
- this interaction must never affect message generation or persistence

---

## Implementation Boundary

### Files

- Modify: `src/AgentX.App/ViewModels/ChatMessageItem.cs`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `src/AgentX.App/Views/ChatPage.xaml.cs`
- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

### Boundary rules

- keep the interaction on the existing inspector surface
- keep the strip and inspector state paths intentionally separate
- do not add persistence or migrations

---

## Verification Scope

- focused chat view-model tests for:
  - inspecting a message note opens the inspector
  - older message inspection does not change strip state
  - missing-snapshot message inspection is a no-op
- WinUI build with `RuntimeIdentifier=win-x64`

No migration, analytics, or service-layer expansion is required.
