# Chat Context Durable Recall Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-chat-context-durable-recall-design.md`

---

## Task 1: Extend context-assembly request and diagnostics

**Files**

- Modify: `src/AgentX.Core/AI/Context/Models/ContextAssemblyModels.cs`

- add `ConversationId` to `ContextAssemblyRequest`
- add durable-recall diagnostics fields for recalled match count and skip reason

## Task 2: Inject durable recall into context assembly

**Files**

- Modify: `src/AgentX.Core/AI/Context/ContextAssemblyService.cs`

- inject `IConversationRecallService`
- query recall only when current-thread assembly needed budget management
- exclude the active conversation from recall
- append a compact recall block only when it fits remaining budget
- keep failures non-fatal

## Task 3: Pass conversation identity from chat flows

**Files**

- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`

- include `ConversationId` in send/regenerate assembly requests
- keep top-level chat orchestration unchanged otherwise

## Task 4: Add focused tests

**Files**

- Modify: `tests/AgentX.Tests/AI/Context/ContextAssemblyServiceTests.cs`
- Modify: `tests/AgentX.Tests/Services/Chat/ChatServiceContextAssemblyTests.cs`

- verify recall block append behavior
- verify active-conversation exclusion
- verify clean degradation on recall failure
- verify chat passes `ConversationId` into assembly

## Task 5: Verify

- run focused context/chat tests
- run focused WinUI build
- confirm clean tree before push
