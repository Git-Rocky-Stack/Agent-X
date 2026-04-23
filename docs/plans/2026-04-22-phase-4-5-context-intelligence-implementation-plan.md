# Phase 4 and 5 Context Intelligence Implementation Plan

**Date:** 2026-04-22
**Derived from:** `docs/plans/2026-04-22-phase-4-5-context-intelligence-design.md`
**Execution style:** backend-first, chat-quality-first

---

## Task 1: Add context-assembly interfaces and models

**Files:**
- Create: `src/AgentX.Core/AI/Context/IContextAssemblyService.cs`
- Create: `src/AgentX.Core/AI/Context/ISemanticContextSelector.cs`
- Create: `src/AgentX.Core/AI/Context/IConversationCompressionService.cs`
- Create: `src/AgentX.Core/AI/Context/Models/ContextAssemblyModels.cs`

- [ ] Define assembly/result models that carry selected messages, overflow summary text, estimated token counts, and fallback flags
- [ ] Keep contracts independent of WinUI and persistence
- [ ] Reuse existing `ChatMessage` and token-estimation helpers where possible

## Task 2: Implement semantic context selection

**Files:**
- Create: `src/AgentX.Core/AI/Context/SemanticContextSelector.cs`
- Create: `tests/AgentX.Tests/AI/Context/SemanticContextSelectorTests.cs`

- [ ] Implement ranking over historical messages using embedding similarity, lexical fallback, recency, adjacency, and role weighting
- [ ] Preserve recent anchors before ranking older history
- [ ] Enforce token-budget limits using the existing token-estimation approach
- [ ] Add tests proving relevant older turns outrank irrelevant recent turns

## Task 3: Implement overflow compression

**Files:**
- Create: `src/AgentX.Core/AI/Context/ConversationCompressionService.cs`
- Create: `tests/AgentX.Tests/AI/Context/ConversationCompressionServiceTests.cs`

- [ ] Compress only meaningful overflow
- [ ] Use low-temperature chat options and strict output-size guardrails
- [ ] Skip compression when overflow is too small, too noisy, or over latency/token thresholds
- [ ] Add tests for skip conditions and summary-shape behavior

## Task 4: Implement context assembly orchestration

**Files:**
- Create: `src/AgentX.Core/AI/Context/ContextAssemblyService.cs`
- Modify: `src/AgentX.Core/AI/IContextWindowManager.cs`
- Modify: `src/AgentX.Core/AI/ContextWindowManager.cs`
- Create: `tests/AgentX.Tests/AI/Context/ContextAssemblyServiceTests.cs`

- [ ] Refactor `ContextWindowManager` into a token-budget helper plus legacy-fit fallback
- [ ] Implement `ContextAssemblyService` to combine anchors, selector output, optional overflow summary, and legacy fallback
- [ ] Add diagnostics to logs only
- [ ] Add tests for fallback-to-legacy behavior

## Task 5: Integrate chat and regeneration flows

**Files:**
- Modify: `src/AgentX.Core/Services/Chat/ChatService.cs`
- Modify: `src/AgentX.App/App.xaml.cs`
- Create: `tests/AgentX.Tests/Services/Chat/ChatServiceContextAssemblyTests.cs`

- [ ] Route both send and regenerate paths through `IContextAssemblyService`
- [ ] Preserve existing semantic-memory and model-routing behavior
- [ ] Register new services in DI
- [ ] Add integration-style tests for chat-quality preservation and fallback behavior

## Task 6: Add Phase 5 backend primitives

**Files:**
- Create: `src/AgentX.Core/Services/Intelligence/IHierarchicalSummaryService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/HierarchicalSummaryService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/IDuplicateEvidenceService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/DuplicateEvidenceService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/IDocumentSynthesisService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/DocumentSynthesisService.cs`

- [ ] Keep these as reusable backend helpers first
- [ ] Avoid broad public-API churn in existing services
- [ ] Prefer composing existing `IAiService`, `IEmbeddingService`, `ISemanticSearchService`, and data access seams

## Task 7: Adopt new Phase 5 helpers in touched services

**Files:**
- Modify: `src/AgentX.Core/Services/Intelligence/SummaryService.cs`
- Modify: `src/AgentX.Core/Services/Intelligence/DuplicateDetectionService.cs`
- Modify: `src/AgentX.Core/Services/Intelligence/ComparisonService.cs`
- Create: focused tests in `tests/AgentX.Tests/Services/`

- [ ] Use hierarchical summary assembly in `SummaryService`
- [ ] Use duplicate evidence helper in `DuplicateDetectionService`
- [ ] Use synthesis helper in `ComparisonService`
- [ ] Keep behavior backward-compatible at the interface boundary

## Task 8: Verify

- [ ] Run targeted context tests
- [ ] Run touched intelligence-service tests
- [ ] Run a focused `dotnet test` slice across the new and modified areas
- [ ] Confirm the worktree only contains intended changes

---

## Acceptance Criteria

- Chat prompt assembly is no longer purely FIFO-based
- Older relevant turns can survive against irrelevant recent clutter
- Failures degrade to legacy context fitting instead of breaking chat
- Summary, duplicate, and comparison backends have deeper reusable primitives
- Tests cover ranking, compression, fallback, and touched service behavior

---

## Deferred to the Next Project

- persistent conversation-summary storage
- message embeddings for durable recall
- clustering jobs and temporal-analysis tables
- trend materialization and dashboard-oriented persistence
