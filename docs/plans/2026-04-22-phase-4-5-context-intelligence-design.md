# Phase 4 and 5 Context Intelligence Design

**Status:** Approved
**Date:** 2026-04-22
**Scope:** Phase 4 enhanced context management and Phase 5 intelligence-service backend improvements
**Primary goal:** Improve live chat quality first, then reuse the same backend primitives to deepen intelligence services without a broad UI rewrite

---

## Executive Summary

Agent-X already has a strong local-AI base: semantic memory, advanced RAG, and agent orchestration are present. The main remaining weakness for chat quality is prompt assembly. The current runtime path still relies on FIFO-style trimming in `ContextWindowManager`, so older but relevant turns can be dropped while less relevant recent turns survive. Phase 5 services also exist, but many are still shallow wrappers over one-shot prompts or coarse heuristics.

This project takes a modular backend-first approach:

- replace FIFO-only context selection with semantic ranking, recent-anchor retention, and overflow summarization
- keep public app surfaces stable
- improve existing intelligence services by introducing reusable backend primitives rather than adding new UI-first features
- explicitly defer durable intelligence infrastructure to a follow-on project

---

## Goals

### Primary goals

- Improve chat response quality on longer conversations
- Preserve high-value prior turns more reliably than raw recency trimming
- Fail soft when semantic ranking or compression is unavailable
- Build reusable backend primitives that can be adopted by Summary, Duplicate Detection, and Comparison services

### Secondary goals

- Add prompt-assembly diagnostics to aid tuning
- Keep UI churn minimal in this slice
- Avoid schema changes or persistence work for this iteration

### Non-goals

- No persistent conversation-summary tables
- No message-level embedding storage
- No durable clustering or temporal-analysis tables
- No large Quick Actions, Comparison, or Knowledge Graph redesign

---

## Architecture

The chat path remains centered on `ChatService`, but prompt assembly moves into a dedicated backend layer:

`ChatService` -> `IContextAssemblyService` -> `ISemanticContextSelector` + `IConversationCompressionService` + memory context + existing chat options -> `IAiService`

### Design principles

- `ContextWindowManager` becomes a token-budget helper, not the policy owner
- selection policy is explicit and testable
- recent conversation continuity is preserved even when semantic ranking is active
- overflow is compressed only when it adds value and fits latency/token constraints
- fallback behavior preserves today’s reliability if newer logic fails

---

## Component Design

### 1. `IContextAssemblyService`

The orchestration layer used by `ChatService` and regeneration flows.

Responsibilities:

- preserve system prompt handling
- preserve the latest exchange window
- call semantic selection for older history
- call overflow summarization when relevant content still does not fit
- return an assembled prompt package plus lightweight diagnostics

Proposed output:

- selected messages
- optional compressed overflow note
- estimated token counts
- fallback flags

### 2. `ISemanticContextSelector`

Selects the most relevant older turns for the current user query.

Responsibilities:

- score historical messages by semantic relevance to the current turn
- bias for adjacency so paired user-assistant turns stay coherent
- bias for message role and recency without letting recency dominate
- respect token budgets

Ranking inputs:

- embedding similarity to current query
- lexical overlap fallback when embeddings fail
- recency weighting
- adjacency/turn-pair weighting
- role weighting

### 3. `IConversationCompressionService`

Produces a short carry-forward summary when relevant overflow content remains after selection.

Responsibilities:

- compress only when the overflow has meaningful semantic value
- generate a compact context note for the current prompt
- skip summarization when it would exceed budget or add too much latency

This summary is prompt-time only and is not persisted.

### 4. Phase 5 backend primitives

These services deepen existing intelligence features without forcing new UI work:

#### `IHierarchicalSummaryService`

- section-aware and chunk-aware summarization assembly
- supports document, section, and condensed summary layers
- can be used internally by `SummaryService`

#### `IDuplicateEvidenceService`

- combines exact-hash grouping with stronger semantic evidence
- provides richer evidence for near-duplicate groups
- supports better ranking and explainability inside `DuplicateDetectionService`

#### `IDocumentSynthesisService`

- shared backend for cross-document synthesis and insight extraction
- can be used by `ComparisonService` now and expanded later

---

## Runtime Data Flow

### Chat send flow

1. Persist the new user message.
2. Load conversation history and system prompt.
3. Load semantic memory context when available.
4. Build raw `ChatMessage` history.
5. Preserve recent anchors.
6. Score older turns with `ISemanticContextSelector`.
7. If useful overflow remains, compress it with `IConversationCompressionService`.
8. Assemble the final prompt package.
9. Send the assembled prompt through the existing `IAiService`.

### Regeneration flow

The regeneration path uses the same assembly service so context behavior is consistent between first-run and regenerate flows.

---

## Fallback Behavior

This project must degrade gracefully.

### Failure policy

- If semantic scoring fails, fall back to lexical overlap plus recency weighting.
- If compression fails, skip the summary and use selected raw turns only.
- If the full context-assembly path fails, fall back to the current `ContextWindowManager` behavior so chat still returns a response.

### Diagnostics

Log-only diagnostics for this slice:

- selected message count
- summarized message count
- estimated token budget
- fallback used
- compression skipped reason

No new diagnostics UI is required in this project.

---

## Verification Strategy

### Unit tests

- semantic ranking prefers relevant older turns over irrelevant recent clutter
- recent anchors are always preserved
- token budget is enforced
- lexical fallback works when embeddings fail
- compression is skipped when low value or over budget
- context assembly falls back to legacy behavior on failure

### Integration tests

- `ChatService` keeps an older relevant turn that would have been lost by FIFO trimming
- regeneration uses the same assembly behavior
- service outputs remain stable when semantic selection is unavailable

### Phase 5 touched-area tests

- hierarchical summary assembly returns layered output
- duplicate evidence groups expose better support for near-duplicate matches
- document synthesis helpers produce stable input/output contracts for comparison flows

---

## Implementation Scope

### Included in this project

- semantic context selection
- recent-anchor retention
- overflow summarization
- chat/regeneration integration
- modular intelligence backend primitives for summaries, duplicate evidence, and synthesis
- focused tests

### Explicitly excluded from this project

- durable conversation-summary persistence
- message embedding tables
- stored clustering jobs
- stored temporal-trend materialization
- large UI additions or new pages

---

## Next Enhancement Project

The heavier persistent-intelligence path remains valid and should be the next enhancement project after this modular backend slice ships.

### Future project: Persistent Intelligence Layer

- persistent conversation summaries
- message-level embeddings for durable semantic recall
- stored clustering and temporal-analysis tables
- trend and synthesis materialization for dashboards and long-horizon reasoning
- richer retrieval over persisted summaries rather than prompt-time-only compression

This follow-on project upgrades the transient context-intelligence layer from this phase into durable, queryable, and longitudinal intelligence infrastructure.

---

## Recommendation

Proceed with the modular context-intelligence approach now. It targets the highest-impact gap for chat quality, keeps risk controlled, avoids schema churn, and creates reusable backend seams that the next persistent-intelligence project can later adopt instead of replacing wholesale.
