# Persistent Intelligence Layer Implementation Plan

**Date:** 2026-04-22
**Derived from:** `docs/plans/2026-04-22-persistent-intelligence-layer-design.md`
**Execution style:** durable-summary-first, analytics-surface-second

---

## Task 1: Add durable summary entities and EF model wiring

**Files:**
- Create: `src/AgentX.Core/Data/Entities/ConversationSummarySnapshotEntity.cs`
- Create: `src/AgentX.Core/Data/Entities/ConversationSummaryStateEntity.cs`
- Modify: `src/AgentX.Core/Data/AgentXDbContext.cs`
- Create: new EF migration under `src/AgentX.Core/Data/Migrations/`

- [ ] Add immutable snapshot storage for summary outputs tied to message ranges
- [ ] Add per-conversation current-state storage for freshness and failure state
- [ ] Configure indexes for `ConversationId`, current snapshot lookup, and refresh timestamps
- [ ] Keep deletes cascading safely from conversations into durable summary rows

## Task 2: Add persistent summary service contracts and models

**Files:**
- Create: `src/AgentX.Core/Services/Intelligence/IConversationSummaryService.cs`
- Create: `src/AgentX.Core/Services/Intelligence/Models/ConversationSummaryModels.cs`

- [ ] Define overview/result models for Analytics consumption
- [ ] Define refresh-state enums and recent-summary inspector contracts
- [ ] Keep contracts independent from WinUI types

## Task 3: Implement durable summary refresh service

**Files:**
- Create: `src/AgentX.Core/Services/Intelligence/ConversationSummaryService.cs`
- Modify: `src/AgentX.App/App.xaml.cs`

- [ ] Implement first-snapshot generation for unsummarized conversations
- [ ] Implement incremental refresh from prior snapshot plus unsummarized tail
- [ ] Persist summary text, key points, message coverage, and refresh metadata
- [ ] Preserve prior snapshot on refresh failure and record error state
- [ ] Register the new service in DI

## Task 4: Integrate stale-state updates with conversation writes

**Files:**
- Modify: `src/AgentX.Core/Services/Chat/ConversationService.cs`
- Modify: any other message-write paths that bypass `ConversationService`

- [ ] Mark durable summaries stale after message persistence
- [ ] Update pending unsummarized counts without blocking chat
- [ ] Keep chat reliability independent from summary-generation success

## Task 5: Extend Analytics backend for conversation intelligence

**Files:**
- Modify: `src/AgentX.Core/Services/Analytics/IAnalyticsService.cs`
- Modify: `src/AgentX.Core/Services/Analytics/AnalyticsService.cs`
- Modify: `src/AgentX.Core/Services/Analytics/Models/AnalyticsModels.cs`

- [ ] Add overview queries for summarized/current/stale/pending counts
- [ ] Add recent durable-summary inspector query results
- [ ] Reuse the durable summary service where appropriate instead of duplicating refresh logic

## Task 6: Surface durable summaries in Analytics UI

**Files:**
- Modify: `src/AgentX.App/ViewModels/AnalyticsViewModel.cs`
- Modify: `src/AgentX.App/Views/AnalyticsPage.xaml`

- [ ] Add `Conversation Intelligence` overview cards
- [ ] Add recent conversation summaries inspector with freshness badges
- [ ] Add empty-state and failed/stale display behavior
- [ ] Keep the new section consistent with existing Analytics visual language

## Task 7: Add focused tests

**Files:**
- Create: `tests/AgentX.Tests/Services/ConversationSummaryServiceTests.cs`
- Create: `tests/AgentX.Tests/Services/AnalyticsServiceConversationSummaryTests.cs`
- Create: `tests/AgentX.Tests/ViewModels/AnalyticsViewModelConversationSummaryTests.cs`

- [ ] Cover first snapshot creation
- [ ] Cover incremental refresh and stale-state advancement
- [ ] Cover failed refresh preserving prior current snapshot
- [ ] Cover Analytics aggregation and inspector ordering
- [ ] Cover view-model population and empty-state behavior

## Task 8: Verify

- [ ] Run migration/build validation for the new schema
- [ ] Run targeted persistence, analytics, and view-model tests
- [ ] Run a focused WinUI build
- [ ] Confirm the worktree only contains intended changes

---

## Acceptance Criteria

- Durable conversation summaries are stored in the database
- Each conversation has explicit summary freshness state
- New messages mark summaries stale without breaking chat
- Analytics shows overview cards and recent durable summary previews
- Refresh failures preserve prior snapshot data and surface readable degraded state
- Tests cover snapshot creation, incremental refresh, failure handling, and Analytics surfacing

---

## Deferred to Later Persistent-Intelligence Slices

- message embeddings for durable semantic recall
- clustering jobs
- temporal-analysis tables
- trend materialization beyond summary freshness and recency
- richer retrieval across persisted summary groups
