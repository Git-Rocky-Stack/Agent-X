# Conversation Theme Clustering Design

**Status:** Approved  
**Date:** 2026-04-23  
**Scope:** Durable snapshot-first conversation theme clustering with Analytics surfacing

---

## Goal

Extend the Persistent Intelligence Layer beyond durable summaries and durable recall by materializing cross-conversation themes that can be queried and surfaced in Analytics.

This slice should:

- cluster conversations from their latest durable summary snapshot
- persist cluster and membership data instead of recomputing it on every Analytics load
- expose theme-level activity signals in Analytics
- keep clustering independent from chat and summary persistence success

---

## Recommended Approach

Use **snapshot-first conversation theme clustering**.

Why:

- it fits the current durable-summary architecture cleanly
- it uses one stable representation per conversation instead of noisy raw message streams
- it creates a durable materialization layer that later temporal analysis can extend

Alternatives explicitly rejected for this slice:

- raw message-level clustering as the primary source
- on-demand clustering with no persistence

---

## Architecture

`ConversationSummaryService` refresh -> ensure latest snapshot embedding -> `ConversationThemeClusterService` materializes clusters and memberships -> `AnalyticsService` reads theme overview and cluster rows -> `AnalyticsViewModel` / `AnalyticsPage`

### Source of truth

- latest `ConversationSummarySnapshotEntity` per conversation
- current conversation metadata such as `UpdatedAt`
- existing durable message-embedding coverage only as secondary context, not as the primary clustering source

### New durable entities

#### `ConversationThemeClusterEntity`

Stores one materialized conversation theme.

Proposed fields:

- `Id`
- `Label`
- `PreviewText`
- `KeyPointsJson`
- `ConversationCount`
- `ActiveConversationCount7d`
- `ActiveConversationCount30d`
- `FirstSeenAt`
- `LastActiveAt`
- `MaterializedAt`

#### `ConversationThemeMembershipEntity`

Stores the latest cluster assignment for a conversation snapshot.

Proposed fields:

- `ConversationId`
- `SnapshotId`
- `ClusterId`
- `SimilarityScore`
- `AssignedAt`

### Snapshot embedding extension

Add a durable embedding to `ConversationSummarySnapshotEntity` so clustering can operate on one persisted vector per latest snapshot instead of rescanning raw messages.

---

## Materialization Flow

### Trigger

- a durable conversation summary refresh makes that conversation eligible for theme re-materialization
- Analytics may opportunistically refresh a small bounded number of stale cluster assignments

### Assignment flow

1. Load the latest durable snapshot for the conversation.
2. Ensure that snapshot has an embedding generated from summary text or summary-plus-key-points payload.
3. Compare the snapshot embedding against current cluster centroids.
4. If similarity exceeds the configured threshold, assign or update membership in the best cluster.
5. Otherwise create a new cluster.
6. Recompute aggregate cluster fields from current members.

### Aggregate cluster fields

- label
- preview text
- key points
- conversation count
- active conversations in the last 7 days
- active conversations in the last 30 days
- first seen timestamp
- last active timestamp

---

## Labeling Strategy

The first pass should stay heuristic and deterministic.

- derive label and preview from repeated high-signal phrases in member summary previews and key points
- persist generated label/preview/key points as materialized values
- defer AI-generated cluster naming to a later slice

This keeps the slice testable, cheap, and inspectable.

---

## Analytics Surface

Add a new `Conversation Themes` block under the existing `Conversation Intelligence` section on the Analytics page.

### Overview cards

- `Active Theme Clusters`
- `Clustered Conversations`
- `New Themes (7d)`
- `Last Materialized`

### Cluster list rows

Each row should show:

- cluster label
- short preview
- conversation count
- active conversations in `7d`
- active conversations in `30d`
- last active timestamp
- top key-point chips or compact text
- a short preview of recent conversations in that theme

### UI principles

- keep the first surface read-only
- make clusters feel like durable intelligence, not admin internals
- preserve current Analytics information architecture instead of creating a new page

---

## Failure Handling

- chat and summary refresh must never depend on clustering
- summary snapshots still persist if snapshot embedding generation fails
- cluster assignment failures leave previous cluster data intact
- Analytics shows empty or stale cluster data rather than hiding conversation intelligence altogether
- repeated materialization for the same latest snapshot should be idempotent

---

## Verification

### Service coverage

- a new summary snapshot gets an embedding and a cluster membership
- similar snapshots land in the same cluster
- distant snapshots create a new cluster
- membership updates when a conversation gets a newer latest snapshot
- cluster activity counts for `7d` and `30d` aggregate correctly

### Analytics coverage

- overview counts and top clusters project correctly from persisted clusters and memberships
- empty-state behavior is stable when summaries exist but no clusters materialize yet

### View-model coverage

- Analytics maps cluster cards and cluster rows correctly
- empty and partial states do not break the rest of the page

---

## Included

- durable cluster and membership tables
- snapshot-level embeddings for clustering
- bounded cluster re-materialization flow
- Analytics overview cards and cluster list
- focused service, Analytics, and view-model tests

## Deferred

- manual cluster merge/split/relabel tools
- historical cluster lineage
- full temporal trend tables
- chat-side theme surfacing
- AI-generated cluster naming

---

## Recommendation

Proceed with snapshot-first conversation theme clustering. It is the lowest-risk way to turn durable summaries and embeddings into durable cross-conversation intelligence, and it sets up later trend materialization without forcing a raw-message clustering system too early.
