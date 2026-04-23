# Chat Inline Context Note Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for session-only per-message inline context notes

---

## Objective

Move the chat-side context story one step closer to the answer by attaching a compact inline note to assistant messages that have an associated assembled-context snapshot.

This slice keeps the feature session-only. It does not add message-level persistence for context snapshots.

---

## Approved Scope

### User experience shape

- show a compact inline context note on assistant messages
- the note appears once an assistant message has a captured context snapshot
- the note persists on those assistant messages for the current app session
- the note reappears when switching away from and back to a conversation during the same session

### Explicitly out of scope

- database persistence for per-message snapshots
- recreating inline notes after app restart
- per-token or streaming-time inline context state
- a second message bubble or heavy inline inspector

---

## Chosen UX Direction

### Approved approach

Use a compact metadata-style inline note under assistant content and above the message meta row.

### Why this was chosen

- it places the explanation beside the answer
- it reuses the already-approved `Context Story` sentence and chips
- it avoids turning the message stream into an analytics surface
- it keeps the slice bounded to session memory instead of opening a new persistence project

---

## Behavior

### Visibility rules

- only assistant messages can show the inline note
- the note appears only when that assistant message has an associated context snapshot
- it stays hidden while the assistant message is still streaming
- user and system messages never show the note

### Session behavior

- when an assistant response completes, the note attaches to that message
- when the user switches conversations and later returns, any assistant messages that already have a stored in-memory mapping for the session regain their note
- after app restart, older assistant messages load normally without an inline note in this slice

### Content

- reuse the existing derived `Context Story` sentence
- reuse the same small source-chip set already approved for strip and inspector surfaces
- keep the inline version visually lighter and shorter than the inspector

---

## Data Flow

### Approved direction

Add a session-only per-message context association keyed by persisted `MessageId`.

### Flow

1. Chat generation completes with a captured `ChatContextInspectionSnapshot`.
2. The completion path also surfaces the persisted assistant `MessageId`.
3. `ChatViewModel` stores the snapshot in an in-memory dictionary keyed by `MessageId`.
4. The just-completed streaming assistant message receives the inline note immediately.
5. When messages are later loaded for the same conversation in the same session, the view model reapplies the note for any assistant message whose `MessageId` exists in the dictionary.

### Persistence boundary

- no new schema
- no migration
- no reload of older notes after app restart
- no background reconciliation

---

## UI Treatment

Place the inline note:

- below assistant message markdown content
- above the existing timestamp/token meta row

The note should read like compact message metadata:

- subtle border or surface
- short caption such as `Context Used`
- one story sentence
- small chips when present

It should feel lighter than the strip and much lighter than the inspector.

---

## Implementation Boundary

### Files

- Modify: `src/AgentX.App/ViewModels/ChatMessageItem.cs`
- Modify: `src/AgentX.App/ViewModels/Coordinators/IMessagingCoordinator.cs`
- Modify: `src/AgentX.App/ViewModels/Coordinators/MessagingCoordinator.cs`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`
- Modify focused coordinator tests only if needed by the `AssistantMessageId` seam

### Boundary rules

- do not add persistence
- do not redesign the whole assistant message layout
- do not create a separate explanation model apart from the existing inspection snapshot

---

## Verification Scope

- focused chat view-model coverage for:
  - attach-on-complete
  - hidden while streaming
  - session-only persistence across conversation switching
  - no note on messages without mapped snapshots
- focused coordinator coverage if the completion payload changes
- WinUI build with `RuntimeIdentifier=win-x64`

No migration or analytics verification is required.
