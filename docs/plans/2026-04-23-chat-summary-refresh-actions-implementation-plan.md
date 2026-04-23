# Chat Summary Refresh Actions Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-chat-summary-refresh-actions-design.md`

---

## Task 1: Add a shared refresh result seam to chat service

**Files**

- Modify: `src/AgentX.Core/Services/Chat/Models/ChatContextInspectionModels.cs`
- Modify: `src/AgentX.Core/Services/Chat/IChatService.cs`
- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`

**Work**

- add a lightweight refresh-result model for one conversation
- expose a chat-service method that:
  - refreshes the durable summary for one conversation
  - re-reads summary inspection data
  - updates the latest cached inspection snapshot when possible
  - creates a limited summary-only snapshot when a refreshed summary exists but no prior inspection snapshot exists
  - preserves prior cached state on failure

---

## Task 2: Add refresh state and command to chat view model

**Files**

- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`

**Work**

- add refresh-state properties
- add a conversation-scoped `RefreshConversationSummaryCommand`
- drive strip and inspector status from:
  - the latest inspection snapshot
  - transient refresh state
- keep the action hidden or disabled when no active conversation exists

---

## Task 3: Surface refresh in both chat UI locations

**Files**

- Modify: `src/AgentX.App/Views/ChatPage.xaml`

**Work**

- add a compact refresh action to the visible intelligence strip
- keep it visible only when useful or while retrying
- add a refresh action to the inspector durable-summary card
- show inline loading/failure state in the summary card

---

## Task 4: Add focused tests

**Files**

- Modify: `tests/AgentX.Tests/Services/Chat/ChatServiceContextAssemblyTests.cs`
- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

**Work**

- verify chat-service refresh success updates cached inspection state
- verify chat-service refresh failure preserves prior inspection snapshot
- verify view-model refresh success clears stale/unavailable state
- verify refresh failure surfaces retry state without clearing current intelligence

---

## Task 5: Verify

**Verification**

- run:
  - `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --filter "FullyQualifiedName~ChatServiceContextAssemblyTests|FullyQualifiedName~ChatViewModelTests" --no-restore`
  - `dotnet build src/AgentX.App/AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
- confirm repo state after the slice

---

## Notes

- this slice does not change persistence
- it is intentionally limited to manual summary refresh affordances
- future work can build on this seam for richer summary inspection or broader context-story surfaces
