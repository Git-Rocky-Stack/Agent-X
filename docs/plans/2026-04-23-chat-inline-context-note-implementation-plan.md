# Chat Inline Context Note Implementation Plan

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Depends on:** context inspector, visible intelligence strip, shared context story

---

## Goal

Attach a compact inline context note to assistant messages that have a captured context snapshot, and keep those notes available while the app session remains active.

---

## Scope

### In scope

- surface a persisted assistant `MessageId` in the send completion path
- keep an in-memory `MessageId -> ChatContextInspectionSnapshot` association in `ChatViewModel`
- map inline context-note text and chips onto assistant `ChatMessageItem`s
- render the note inside the assistant message template
- add focused regression coverage

### Out of scope

- database persistence for per-message snapshots
- recreating notes after restart
- richer inline inspector behavior

---

## Implementation Steps

### 1. Expose the assistant message identity on completion

Files:

- `src/AgentX.App/ViewModels/Coordinators/IMessagingCoordinator.cs`
- `src/AgentX.App/ViewModels/Coordinators/MessagingCoordinator.cs`

Work:

- add nullable `AssistantMessageId` to the send result and streaming completion args
- after a persisted send completes, resolve the newly written assistant message ID using the existing conversation service
- leave fallback/direct-stream cases nullable

### 2. Extend the message view model

File:

- `src/AgentX.App/ViewModels/ChatMessageItem.cs`

Work:

- add inline-note text
- add inline-note chip collection
- add message-level visibility helpers
- keep the message item self-contained for binding

### 3. Store and reapply session-only mappings

File:

- `src/AgentX.App/ViewModels/ChatViewModel.cs`

Work:

- maintain a private in-memory dictionary keyed by `MessageId`
- on streaming completion:
  - set the completed assistant message ID
  - attach the inline note to the streaming assistant message
  - store the snapshot in the dictionary when `MessageId` is available
- on conversation load:
  - map messages normally
  - reapply inline note state for assistant messages that already have a stored mapping in the session

### 4. Render the inline note

File:

- `src/AgentX.App/Views/ChatPage.xaml`

Work:

- add a compact inline note under assistant content and above the meta row
- show caption, story text, and chips
- hide while streaming or when no inline note exists

### 5. Verify

Files:

- `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`
- `tests/AgentX.Tests/ViewModels/Coordinators/MessagingCoordinatorTests.cs` if needed

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~MessagingCoordinatorTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- keep the mapping session-only
- do not add schema or migration work
- do not duplicate context-story derivation logic
- keep the inline note visually subordinate to the actual answer
