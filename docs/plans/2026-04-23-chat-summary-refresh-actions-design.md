# Chat Summary Refresh Actions Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for manual summary refresh actions

---

## Objective

Add a safe, conversation-scoped `Refresh Summary` action to the chat surface so users can recover stale, pending, or unavailable durable summary state without leaving chat.

This slice is intentionally narrow. It builds on the shipped context inspector and visible intelligence strip without introducing new persistence, background orchestration, or refresh-all behavior.

---

## Approved Scope

### User experience shape

- expose one shared `Refresh Summary` action
- surface it in both:
  - the visible intelligence strip
  - the inspector durable-summary block
- keep it conversation-scoped and read-only outside the refresh itself

### Explicitly out of scope

- refresh-all operations
- automatic retries
- background refresh daemons
- schema changes
- new analytics surfaces
- multi-conversation orchestration UI

---

## Chosen UX Direction

### Approved approach

Use a single shared refresh command with two different UI weights:

- visible strip:
  - show the action only when refresh is useful
  - compact, low-noise action
- inspector:
  - always show the action inside the durable-summary section
  - keep the richer status and explanation nearby

### Why this was chosen

- users get direct recovery from the main chat surface
- the visible strip stays low-noise when the summary is already current
- the inspector remains the deeper troubleshooting surface

---

## Refresh Command Behavior

The action is safe, bounded, and conversation-scoped.

### Command rules

- only targets the active conversation
- reuses the existing durable summary refresh service
- does not block message generation or conversation persistence
- never triggers repeated retries automatically

### Success behavior

- refresh the durable summary for the active conversation
- re-read the summary inspection
- update:
  - the visible intelligence strip
  - the inspector durable-summary section
  - the cached latest in-memory inspection snapshot for that conversation

### Failure behavior

- preserve the prior inspection snapshot
- preserve current strip/inspector content
- surface a short retry-oriented error state instead of clearing intelligence

---

## Execution States

The shared refresh action uses three explicit states:

- `idle`
- `refreshing`
- `refresh failed`

### While refreshing

- disable the refresh action
- visible strip status becomes `Refreshing durable summary...`
- inspector summary block shows the same in-progress state

### On failure

- visible strip shows a concise retry state
- inspector keeps the richer summary status and adds the refresh error
- the refresh action remains available for retry

---

## Backend And UI Boundary

### Backend

- reuse `IConversationSummaryService.RefreshConversationSummaryAsync(conversationId, ct)`
- reuse `GetConversationSummaryInspectionAsync(...)`
- allow a tiny chat-service seam to refresh and update the latest cached inspection snapshot for one conversation

### View-model

- add conversation-scoped refresh state:
  - `IsRefreshingConversationSummary`
  - `ConversationSummaryRefreshError`
  - `CanRefreshConversationSummary`
  - `ShowConversationSummaryRefreshAction`
- derive strip and inspector behavior from the same current inspection snapshot plus transient refresh state

### UI

- visible strip:
  - show refresh only when stale, pending, unavailable, refreshing, or failed
- inspector:
  - always expose the refresh button in the durable-summary section
  - show inline loading and failure text

---

## Implementation Boundary

### Files

- Modify: `src/AgentX.Core/Services/Chat/Models/ChatContextInspectionModels.cs`
- Modify: `src/AgentX.Core/Services/Chat/IChatService.cs`
- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`
- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`
- Modify: `src/AgentX.App/Views/ChatPage.xaml`
- Modify: `tests/AgentX.Tests/Services/Chat/ChatServiceContextAssemblyTests.cs`
- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

### Intent

- add one tiny shared seam rather than teaching the view model to mutate cached inspection state directly
- avoid any redesign of recall/compression sections
- keep the slice focused on durable summary refresh affordances

---

## Verification Scope

- focused chat-service tests for refresh result handling
- focused chat view-model tests for refresh visibility, success, and failure
- WinUI build with `RuntimeIdentifier=win-x64`

No migration or analytics verification is required for this slice.
