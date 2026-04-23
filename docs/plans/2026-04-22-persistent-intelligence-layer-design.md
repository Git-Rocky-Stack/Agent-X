# Persistent Intelligence Layer Design

**Status:** Approved
**Date:** 2026-04-22
**Scope:** Durable conversation summaries first, with an Analytics inspection surface
**Primary goal:** Add persistent conversation-summary infrastructure that survives beyond prompt-time assembly and expose it through the existing Analytics page

---

## Executive Summary

Agent-X now has stronger transient intelligence: prompt assembly is smarter, summary/duplicate/comparison backends are deeper, and several existing surfaces expose those improvements. The next gap is durability. Conversation intelligence still disappears back into the live chat flow instead of becoming queryable application state.

This project starts the Persistent Intelligence Layer with the smallest durable slice that meaningfully compounds:

- persist rolling conversation summaries as durable snapshots
- track freshness, stale state, and refresh failures per conversation
- keep updates incremental instead of rebuilding full transcripts every time
- surface the resulting intelligence in Analytics as overview cards plus a recent-summary inspector
- explicitly defer message embeddings, clustering, and long-horizon materialization until later follow-on slices

---

## Goals

### Primary goals

- Persist conversation summaries beyond prompt-time generation
- Refresh summaries incrementally as conversations evolve
- Keep summary refresh state visible and inspectable
- Add a broader non-chat UI surface for durable conversation intelligence

### Secondary goals

- Preserve prior summary snapshots for inspection and later analysis
- Make stale and failed summary states explicit
- Build seams that later embedding/clustering/trend layers can adopt

### Non-goals

- No message-level embedding storage in this slice
- No stored clustering jobs
- No temporal trend materialization tables
- No chat-page redesign or inline chat inspector for durable summaries
- No branch-aware summary merging across related conversations

---

## Architecture

The first durable intelligence path sits beside current chat persistence rather than inside prompt assembly:

`ConversationService` / message writes -> `IConversationSummaryService` state invalidation -> durable summary refresh -> summary snapshot/state tables -> `AnalyticsService` query surface -> `AnalyticsViewModel` / `AnalyticsPage`

### Design principles

- chat persistence must not depend on summary-generation success
- summary snapshots are immutable once written
- current summary state is cheap to query and cheap to mark stale
- incremental refresh should prefer prior snapshot + unsummarized tail over full transcript rebuilds
- Analytics is the first read surface, but the backend contracts should remain reusable

---

## Component Design

### 1. `ConversationSummarySnapshotEntity`

Immutable snapshot rows representing a durable summary refresh result.

Proposed fields:

- `Id`
- `ConversationId`
- `StartSortOrder`
- `EndSortOrder`
- `SummaryText`
- `KeyPointsJson`
- `MessageCount`
- `EstimatedTokenCount`
- `SourceFingerprint`
- `RefreshReason`
- `CreatedAt`

Responsibilities:

- preserve historical summary outputs
- provide direct inspector data for Analytics
- establish a durable source for future clustering and temporal analysis

### 2. `ConversationSummaryStateEntity`

Single current-state row per conversation.

Proposed fields:

- `ConversationId`
- `CurrentSnapshotId`
- `LastSummarizedSortOrder`
- `PendingMessageCount`
- `Status`
- `LastAttemptAt`
- `LastSuccessfulRefreshAt`
- `LastError`

Responsibilities:

- indicate whether a conversation is current, stale, pending, or failed
- support cheap “what needs refresh?” queries
- keep current summary lookup independent from snapshot history traversal

### 3. `IConversationSummaryService`

Primary backend seam for durable summary refresh and retrieval.

Core responsibilities:

- mark a conversation stale after message writes
- build a first durable snapshot for unsummarized conversations
- refresh from prior snapshot plus unsummarized tail
- return overview metrics and recent summary entries for Analytics

Likely methods:

- `EnsureSummaryAsync(conversationId, forceRefresh = false)`
- `MarkConversationStaleAsync(conversationId)`
- `GetRecentSummariesAsync(limit)`
- `GetSummaryOverviewAsync()`

### 4. Analytics query integration

The first UI surface lives on the existing Analytics page.

The backend can either:

- extend `IAnalyticsService` with conversation-intelligence queries, or
- compose a small `IConversationSummaryAnalyticsService` under `AnalyticsViewModel`

Recommendation: keep the conversation-summary refresh logic in `IConversationSummaryService`, and use either direct analytics-query methods there or a thin analytics-facing wrapper if the query set starts growing.

---

## Runtime Data Flow

### Message write path

1. A new message is persisted through existing conversation/message storage.
2. The durable summary state for that conversation is marked stale.
3. Pending unsummarized message count advances.
4. Chat completes normally regardless of summary-refresh status.

### Summary refresh path

1. A caller requests summary freshness or Analytics loads recent conversation intelligence.
2. The service checks summary state.
3. If no snapshot exists, it summarizes the bounded transcript.
4. If a snapshot exists, it loads the prior summary plus the unsummarized tail.
5. AI generates refreshed summary text and key points.
6. A new immutable snapshot row is written.
7. State is updated to point at the new current snapshot.

### Analytics read path

1. Analytics loads standard usage metrics.
2. It additionally loads durable-summary overview counts.
3. It loads recent conversation summary inspector items ordered by latest successful refresh.
4. The page renders overview cards plus recent summary previews with freshness state.

---

## Analytics Surface

The first durable-summary inspector appears as a new `Conversation Intelligence` section on the existing Analytics page.

### Overview cards

- `Summarized Conversations`
- `Current Snapshots`
- `Stale Conversations`
- `Pending Refreshes`
- optional freshness/support label such as `Last successful refresh`

### Recent summaries inspector

Each row should show:

- conversation title
- freshness badge: `Current`, `Stale`, or `Failed`
- last refresh timestamp
- covered message range or message count
- 2-4 line summary preview
- compact key-point chips or short bullets

### UI principles

- treat the section as conversation intelligence, not raw admin state
- keep the summary preview central and the counters secondary
- stay read-only in the first slice; deep actions can come later

---

## Refresh Behavior

### Trigger policy

- message writes mark summaries stale
- summary refresh occurs when:
  - pending message count crosses a threshold
  - Analytics requests recent data and sees stale summaries
  - a future explicit force refresh is requested

### Refresh strategy

- first snapshot: summarize the conversation transcript up to a bounded limit
- subsequent snapshots: summarize from prior snapshot plus unsummarized tail
- persist both summary text and key points to avoid reparsing for the UI

---

## Failure Handling

Failure must never block live chat.

### Rules

- message persistence succeeds even if durable-summary refresh fails
- prior snapshots remain intact on refresh failure
- state records `Failed` or `Stale` status plus `LastError`
- Analytics shows degraded state instead of hiding the conversation
- if AI is unavailable, existing snapshots remain readable

---

## Verification Strategy

### Unit and service tests

- snapshot creation for a conversation with no prior summary
- incremental refresh advances `LastSummarizedSortOrder`
- stale state is set after new messages
- failed refresh preserves prior snapshot and records failure state
- overview metrics aggregate correctly
- recent summary inspector queries return the expected rows and ordering

### UI/view-model tests

- Analytics view model loads durable-summary cards and inspector items
- empty-state behavior is stable when no durable summaries exist
- stale and failed badges surface correctly from service data

### Build verification

- WinUI build remains clean apart from existing unrelated warnings
- targeted test slice covers new persistence, analytics, and view-model wiring

---

## Implementation Scope

### Included in this project

- durable conversation-summary snapshots
- current summary state tracking
- incremental summary refresh path
- Analytics overview cards for conversation intelligence
- recent conversation summaries inspector
- focused persistence, analytics, and view-model tests

### Explicitly excluded from this project

- message embedding tables
- semantic retrieval over per-message embeddings
- clustering jobs
- temporal-analysis tables
- trend materialization for dashboards beyond summary freshness counts

---

## Follow-On Slices

After this first Persistent Intelligence Layer slice lands, later work should add:

- message-level embeddings for durable semantic recall
- clustering and temporal-analysis tables
- trend materialization for long-horizon analytics
- richer retrieval over stored summaries and later embedding-backed summary groups

---

## Recommendation

Proceed with the durable summary snapshot approach first. It is the best balance of immediate value, low schema risk, and future compatibility. It makes conversation intelligence durable and inspectable now, while establishing a persistence model that later embeddings, clustering, and trend systems can extend instead of replacing.
