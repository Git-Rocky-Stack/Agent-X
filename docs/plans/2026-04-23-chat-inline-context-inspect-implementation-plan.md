# Chat Inline Context Inspect Implementation Plan

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Depends on:** context inspector, visible strip, shared context story, inline context notes

---

## Goal

Allow users to open the full context inspector from any assistant message’s inline context note while keeping the visible strip anchored to the latest conversation snapshot.

---

## Scope

### In scope

- add a message-level inspect command
- split strip story state from inspector display state
- make the inline note surface interactive
- add focused regression coverage

### Out of scope

- new persistence
- new inspector surfaces
- message-level history storage

---

## Implementation Steps

### 1. Separate latest strip state from inspector display state

File:

- `src/AgentX.App/ViewModels/ChatViewModel.cs`

Work:

- keep `_latestContextInspection` as the strip source of truth
- add strip-specific story/chip properties derived from `_latestContextInspection`
- allow the inspector display properties to be updated from a selected message snapshot without mutating `_latestContextInspection`

### 2. Add the inspect command

File:

- `src/AgentX.App/ViewModels/ChatViewModel.cs`

Work:

- add one command that accepts a `ChatMessageItem`
- resolve the message’s stored session snapshot by `MessageId`
- apply it to the inspector display state
- open the inspector

### 3. Make the inline note interactive

Files:

- `src/AgentX.App/Views/ChatPage.xaml`
- `src/AgentX.App/Views/ChatPage.xaml.cs`

Work:

- turn the note surface into a lightweight button-like container
- forward activation to the new view-model command
- keep the visual treatment subtle and consistent with the rest of chat

### 4. Add focused tests

File:

- `tests/AgentX.Tests/ViewModels/ChatViewModelTests.cs`

Work:

- assert inspect-from-inline-note opens the inspector
- assert the inspector pivots to the selected message snapshot
- assert the strip story remains on the latest snapshot
- assert missing-snapshot note inspection does nothing

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~ChatViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- do not mutate latest-strip state when inspecting an older message
- do not introduce a second source of truth for snapshot derivation
- keep the interaction purely session-scoped
