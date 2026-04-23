# Chat Context Durable Recall Design

**Status:** Approved  
**Date:** 2026-04-23  
**Scope:** Backend-only durable cross-conversation recall injection into chat context assembly

---

## Goal

Reuse the new durable message-embedding layer during chat prompt assembly without introducing a second prompt-building path or a new chat UI surface.

This slice is intentionally narrow:

- inject recall through `IContextAssemblyService`
- search only `other conversations`
- keep recalled content out of the live chat chronology
- defer chat inspector and explainability UI to a later slice

---

## Recommended Approach

Inject durable recall inside `ContextAssemblyService`, not `ChatService`.

Why:

- context policy stays centralized in one assembly layer
- send and regenerate flows inherit the same behavior automatically
- the recall seam composes cleanly with existing semantic selection, recent anchors, and overflow compression

---

## Architecture

`ChatService` -> `IContextAssemblyService` -> current-thread selection/compression + optional durable recall -> `IAiService`

### Rules

- recall runs only when the conversation is large enough to need assembly logic
- recall excludes the current conversation via `excludeConversationId`
- recall is formatted as a compact system-prompt appendix, not as synthetic chat turns
- recall remains best-effort and non-fatal

---

## Data Flow

1. `ChatService` loads current conversation messages and memory context.
2. `ChatService` passes `ConversationId` into `ContextAssemblyRequest`.
3. `ContextAssemblyService` assembles current-thread context as it does today.
4. If the thread exceeded the direct-fit budget, the service optionally queries `IConversationRecallService`.
5. Matching messages from other conversations are formatted into a compact `Durable Cross-Conversation Recall` block.
6. The block is appended to the assembled system prompt only if it fits the remaining augmentation budget.

---

## Retrieval Policy

### Included

- bounded top-K results
- user and assistant messages only
- semantically relevant cross-conversation messages

### Excluded

- current-conversation messages
- raw insertion of recalled messages into live chronology
- aggressive backfill or clustering work in the chat path

### Guardrails

- skip near-duplicates of already selected current-thread content
- skip recall when remaining budget is too small
- skip recall cleanly on embedding/search errors

---

## Prompt Shape

The system prompt may include:

- base system prompt
- memory context
- durable summary context
- condensed earlier-conversation summary
- durable cross-conversation recall

Recall formatting should stay compact:

- conversation title
- role
- short content preview

---

## Diagnostics

Add log/test-visible diagnostics only:

- recalled match count
- recall added or skipped
- recall skip reason

No new chat UI ships in this slice.

---

## Verification

- `ContextAssemblyService` appends recall when matches exist and budget allows
- recall query excludes the active conversation
- recall failures degrade cleanly without breaking assembly
- `ChatService` send/regenerate flows continue routing through the same assembly path

---

## Deferred

- chat-side context inspector
- “why this answer used this context” UI
- stored clustering and trend materialization
- current-thread + recalled-context visual attribution
