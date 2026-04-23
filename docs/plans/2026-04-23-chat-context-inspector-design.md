# Chat Context Inspector Design

**Date:** 2026-04-23  
**Track:** `B2` Chat-Side Intelligence Visibility  
**Status:** Approved design for first implementation slice

---

## Objective

Expose persistent intelligence where users already work: the chat surface.

This first slice is intentionally narrow. It adds a read-only, on-demand chat context inspector that explains what durable intelligence was used for the latest response without introducing new persistence, new long-lived chat layout chrome, or mutation actions.

---

## Approved Scope

### User experience shape

- add a single chat-chrome entry point for context inspection
- open an on-demand inspector rather than a permanent right rail
- keep the first version read-only
- target the latest completed context assembly for the active conversation

### Explicitly out of scope for this slice

- permanent right-side inspector layout
- per-message inline explanation cards
- force-refresh or mutation actions
- context inspection history or replay
- new database persistence for inspector state

---

## Chosen UX Direction

### Recommended and approved approach

Use an `on-demand context inspector` launched from existing chat chrome.

This approach was selected over:

1. inline explanation cards in the message stream
2. a permanently visible right rail

### Why this was chosen

- it makes persistent intelligence visible without turning chat into an admin surface
- it minimizes layout disruption in a page that already has a left conversation rail
- it creates a stable inspection seam that can later power richer inline or docked UI
- it keeps the first `B2` slice tightly scoped to explanation and visibility

---

## Inspector Surface

### Entry point

Add a single `Context` or `Why this response?` entry point in existing chat chrome near prompt and research controls.

### Presentation

The inspector opens on demand and remains a read-only explanation surface in `v1`.

### Behavior

- the inspector shows the latest completed context snapshot for the active conversation
- while a new response is generating, the previous completed snapshot remains visible
- if no captured context exists for the active conversation, the inspector shows an intentional empty state

---

## Inspector Contents

The inspector should emphasize understandable explanation over raw prompt dumps.

### Block 1: Assembly Overview

Show:

- selected message count
- anchor count
- overflow count
- estimated prompt and message tokens
- whether lexical fallback was used
- whether legacy fallback was used

### Block 2: Durable Summary

Show:

- whether a current durable summary exists
- freshness state
- pending/newer message count when stale
- summary preview
- summary key points

### Block 3: Durable Recall

Show:

- whether cross-conversation recall was added
- recalled message count
- recall skip reason when recall was not used
- compact rows for recalled items actually used, including:
  - conversation title
  - role
  - preview text
  - match strength

### Block 4: Compression and Skip Reasons

Show:

- whether overflow compression summary was added
- compression skip reason when absent
- durable recall skip reason when absent
- short plain-English explanation derived from those states

### What is not primary in `v1`

- raw assembled prompt dumps
- editable controls
- drill-through actions

---

## Data Flow and State Model

### Approved direction

Expose the latest `in-memory` context assembly snapshot. Do not persist inspector state in this slice.

### Flow

1. `ChatService` performs context assembly as part of send/regenerate.
2. After `AssembleAsync(...)`, it builds a lightweight `ChatContextInspectionSnapshot`.
3. The snapshot contains:
   - conversation id
   - capture timestamp
   - current query
   - context assembly diagnostics
   - durable summary inspection data
   - durable recall rows actually used
   - human-readable explanation strings derived from flags and skip reasons
4. `MessagingCoordinator` propagates the latest snapshot through the existing send/complete flow.
5. `ChatViewModel` stores the latest snapshot per active conversation and binds the inspector to it.

### Storage boundaries

- no new database table
- no history browser of prior assemblies
- no reconstruction of older assistant turns
- no requirement to persist inspector state across app restarts

### Empty and fallback states

- if the active conversation has no captured snapshot yet, show `No generation context captured yet`
- if direct-stream fallback or a reduced path was used, show a `limited visibility` state instead of implying full assembly diagnostics exist

---

## Failure Handling

The inspector must never block chat generation.

### Principles

- inspection capture is best-effort
- missing summary data should not suppress the rest of the inspector
- missing recall rows should clearly distinguish `not used` from `skipped`
- legacy fallback should be surfaced explicitly
- failures in inspector enrichment should degrade to partial visibility, not broken chat

---

## Verification Scope

### Required test targets

- chat service coverage for latest in-memory context snapshot creation
- coordinator coverage for propagating the latest snapshot through completion state
- focused chat view-model coverage for:
  - empty state
  - populated snapshot mapping
  - conversation switching behavior
  - limited-visibility and fallback states

### Not required for this slice

- migration tests
- persistence tests for inspector state
- per-message history inspection tests

---

## Expected Outcome

After this slice, users should be able to answer:

- what durable intelligence was used
- whether the conversation summary was current or stale
- whether cross-conversation recall contributed context
- why compression or recall may have been skipped

This turns existing persistent intelligence from hidden backend behavior into a visible chat-side product capability without expanding scope into a larger chat redesign.
