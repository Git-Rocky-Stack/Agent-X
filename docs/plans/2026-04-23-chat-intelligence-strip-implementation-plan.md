# Chat Intelligence Strip Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-chat-intelligence-strip-design.md`

---

## Task 1: Add derived strip state to chat view model

**Files**

- Modify: `src/AgentX.App/ViewModels/ChatViewModel.cs`

**Work**

- retain the latest inspection snapshot as the strip source of truth
- add derived properties for:
  - strip visibility
  - badge text
  - status text
  - current/stale/pending/unavailable state flags
- update snapshot apply/reset paths to refresh the derived strip state
- ensure the strip hides when no conversation is active

---

## Task 2: Surface the intelligence strip in chat UI

**Files**

- Modify: `src/AgentX.App/Views/ChatPage.xaml`

**Work**

- insert a compact strip directly under the top toolbar
- render mutually exclusive badge variants for:
  - current
  - stale
  - pending
  - unavailable
- bind the status text to the derived view-model property
- wire `Inspect Context` to the existing context inspector toggle command
- keep the message area and input bar layout intact after row shifts

---

## Task 3: Add focused chat view-model coverage

**Files**

- Modify: `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

**Work**

- verify a current summary maps to the `Current` strip state
- verify a stale summary maps to the `Stale` strip state with pending count text
- verify limited/no-summary states map to `Unavailable`
- verify no active conversation hides the strip

---

## Task 4: Verify

**Verification**

- run:
  - `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --filter "FullyQualifiedName~ChatViewModelTests" --no-restore`
  - `dotnet build src/AgentX.App/AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
- confirm repo state after the slice

---

## Notes

- this is a visibility-only slice
- the existing context inspector remains the deep explanation surface
- force-refresh or summary actions remain future work
