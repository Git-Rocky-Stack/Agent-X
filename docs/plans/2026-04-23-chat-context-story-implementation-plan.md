# Chat Context Story Implementation Plan

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Depends on:** context inspector, visible intelligence strip, summary refresh actions

---

## Goal

Add one shared chat-side `Context Story` sentence and small source-chip set so the user can understand how the latest response context was formed without reading the lower-level inspector metrics first.

---

## Scope

### In scope

- derive story text from the existing in-memory inspection snapshot
- derive compact source chips from existing summary/recall/fallback/compression state
- surface the story in the visible strip
- add a top `Context Story` card in the inspector
- add focused service and view-model regression coverage

### Out of scope

- new persistence
- analytics changes
- per-message explanation UI
- new background orchestration

---

## Implementation Steps

### 1. Extend the inspection model

File:

- `src/AgentX.Core/Services/Chat/Models/ChatContextInspectionModels.cs`

Work:

- add a lightweight source-chip record for chat-side story labels
- add derived story text and derived source-chip properties on `ChatContextInspectionSnapshot`
- keep the derivation local to the inspection model

### 2. Preserve the single snapshot seam

File:

- `src/AgentX.Core/Services/Chat/ChatService.cs`

Work:

- keep using the existing snapshot capture path
- ensure normal and summary-only refresh snapshots expose the new derived story cleanly
- avoid duplicating phrasing logic in service code

### 3. Map the story into the chat view model

File:

- `src/AgentX.App/ViewModels/ChatViewModel.cs`

Work:

- add bindable story text and source-chip collections
- map the new story values inside `ApplyContextInspection(...)`
- provide a neutral active-conversation story when no snapshot exists yet
- keep refresh error state separate from the story text

### 4. Surface the story in chat

File:

- `src/AgentX.App/Views/ChatPage.xaml`

Work:

- expand the intelligence strip center section into:
  - status line
  - story line
  - source chips
- add a new top `Context Story` card inside the inspector before the metric cards
- reuse existing visual primitives and keep the strip compact

### 5. Add focused regression coverage

Files:

- `tests/AgentX.Tests/Services/Chat/ChatServiceContextAssemblyTests.cs`
- `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

Work:

- assert normal assembled-path story/chip derivation
- assert stale story mapping
- assert limited-visibility story mapping
- assert no-snapshot active-conversation story state

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~ChatServiceContextAssemblyTests|FullyQualifiedName~ChatViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

Success means:

- focused chat tests pass
- WinUI build passes
- the new story surfaces compile cleanly and remain bounded to chat

---

## Guardrails

- do not create a new persistence seam
- do not move raw diagnostics into the strip
- keep story text deterministic and derived from snapshot state only
- keep refresh failures as operational status, not story state
