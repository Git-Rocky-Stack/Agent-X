# Chat Context Inspector Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-chat-context-inspector-design.md`

---

## Task 1: Add chat-side context inspection models

**Files**

- Add: `src/AgentX.Core/Services/Chat/Models/ChatContextInspectionModels.cs`

**Work**

- define the in-memory snapshot model for the latest completed context assembly
- include assembly diagnostics, summary inspection data, recall rows, and explanation strings
- keep the models chat-specific and separate from Analytics models

---

## Task 2: Extend chat service with latest inspection exposure

**Files**

- Modify: `src/AgentX.Core/Services/Chat/IChatService.cs`
- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`

**Work**

- add a read-only seam for retrieving the latest inspection snapshot for a conversation
- capture a snapshot after `AssembleAsync(...)` in both send and regenerate paths
- populate a limited-visibility snapshot for reduced or fallback paths when appropriate
- keep the data in memory only

---

## Task 3: Enrich inspection snapshots from existing services

**Files**

- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`
- Modify if needed: `src/AgentX.Core/Services/Chat/ConversationSummaryService.cs`

**Work**

- map durable summary presence, preview, key points, freshness, and pending message count
- map durable recall usage and rows actually included in the assembled context
- translate compression and recall skip reasons into short user-facing explanations
- keep enrichment best-effort and non-blocking

---

## Task 4: Propagate latest inspection state through messaging coordinator

**Files**

- Modify: `src/AgentX.App/ViewModels/Coordinators/IMessagingCoordinator.cs`
- Modify: `src/AgentX.App/ViewModels/Coordinators/MessagingCoordinator.cs`

**Work**

- extend completion payloads or coordinator seams to expose the latest inspection snapshot
- preserve existing streaming behavior
- ensure direct-stream fallback still reports a safe limited-visibility state

---

## Task 5: Add chat view-model state for the inspector

**Files**

- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`

**Work**

- store the latest context inspection snapshot for the active conversation
- add panel open/close state and empty-state properties
- map summary, recall, diagnostics, and explanation strings into UI-ready properties
- reset or swap inspector content correctly when the active conversation changes

---

## Task 6: Surface the on-demand inspector in chat UI

**Files**

- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify code-behind only if required for panel interaction

**Work**

- add the `Context` or `Why this response?` entry point near existing prompt/research controls
- render the on-demand inspector without redesigning the full chat layout
- implement the approved sections:
  - assembly overview
  - durable summary
  - durable recall
  - compression and skip reasons
- provide clear empty and limited-visibility states

---

## Task 7: Add focused tests

**Files**

- Add or modify: chat service tests for latest inspection snapshot creation
- Modify: `tests/AgentX.Tests/ViewModels/Coordinators/MessagingCoordinatorTests.cs`
- Add: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

**Work**

- verify in-memory snapshot capture after send/regenerate
- verify coordinator propagation into completion flow
- verify chat view-model empty, populated, fallback, and conversation-switch states

---

## Task 8: Verify

**Verification**

- run focused chat/coordinator/view-model tests
- run WinUI build with `RuntimeIdentifier=win-x64`
- confirm repo state before commit/push

---

## Notes

- this slice is intentionally read-only
- actions like force-refresh summary or jump-to-source context remain future work
- no persistence should be added unless the project explicitly expands beyond the approved `v1` scope
