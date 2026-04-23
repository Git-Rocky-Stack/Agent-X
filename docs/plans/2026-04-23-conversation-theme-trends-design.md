# Conversation Theme Trends Design

**Status:** Approved  
**Date:** 2026-04-23  
**Scope:** Analytics-only temporal trend materialization for durable conversation themes

---

## Goal

Extend the new durable conversation-theme clustering layer with persistent daily trend metrics that can be queried cheaply and surfaced in Analytics.

This slice should:

- persist daily theme activity rows instead of deriving all trend data on Analytics load
- keep the trend layer incremental and bounded
- surface top-level trend cards plus per-theme daily series in Analytics
- avoid widening scope into Dashboard or chat

---

## Recommended Approach

Use a **daily theme metrics table** materialized from the current cluster layer.

Why:

- it fits the current durable summary -> theme cluster pipeline
- it keeps read performance cheap and predictable
- it avoids prematurely introducing a full event ledger or event-sourcing layer

Alternatives explicitly rejected for this slice:

- full event ledger plus later rollups
- query-time recomputation from conversations, snapshots, and memberships on every Analytics load

---

## Architecture

`ConversationSummaryService` refresh -> `ConversationThemeClusterService` updates memberships/clusters -> `ConversationThemeTrendService` upserts daily metric rows for affected clusters -> `AnalyticsService` reads trend overview + per-theme daily series -> `AnalyticsViewModel` / `AnalyticsPage`

### New durable entity

#### `ConversationThemeDailyMetricEntity`

One row per theme cluster per calendar day.

Proposed fields:

- `ClusterId`
- `Date`
- `ActiveConversationCount`
- `NewConversationCount`
- `SnapshotRefreshCount`
- `MaterializedAt`

Primary key:

- composite `ClusterId + Date`

### Source of truth

- current cluster memberships
- latest summary snapshots for members
- current conversation timestamps

This slice intentionally does not introduce a separate event-history subsystem.

---

## Materialization Behavior

### Trigger policy

- trend materialization is driven by cluster materialization, not chat writes directly
- when a cluster is recomputed, its daily metric rows are upserted
- Analytics may request bounded refresh for a small number of recently touched clusters

### Bounded window

For the first slice, materialize:

- `today`
- the trailing `30` days for any cluster touched during refresh

This gives Analytics a durable 30-day window without forcing a full historical rebuild system.

### Daily row semantics

- `ActiveConversationCount`: number of conversations in the cluster with `UpdatedAt.Date == Date`
- `NewConversationCount`: number of conversations whose current cluster membership `AssignedAt.Date == Date`
- `SnapshotRefreshCount`: number of current member latest snapshots whose `GeneratedAt.Date == Date`
- `MaterializedAt`: last time the row was recalculated

These semantics are intentionally current-state-oriented. Full historical cluster lineage is deferred.

---

## Analytics Surface

Add a `Theme Trends` block directly below `Conversation Themes` on the existing Analytics page.

### Overview cards

- `Trending Themes`
- `New Theme Entries (7d)`
- `Most Active Theme`
- `Last Trend Refresh`

### Per-theme rows

For the top `3-5` themes:

- theme label
- compact activity summary
- momentum / 7-day delta label
- 30-day mini bar strip or sparkline from materialized daily rows
- optional new-entry count for the last week

### UI principles

- keep the slice Analytics-only
- present trends as persistent intelligence, not operational dashboard telemetry
- stay read-only

---

## Failure Handling

- cluster materialization must not depend on trend materialization succeeding
- if trend writes fail, cluster reads still work
- missing or stale trend rows should not break Analytics
- repeated recomputation for the same cluster/day range should be idempotent

---

## Verification

### Service coverage

- touched clusters upsert daily rows
- recomputing the same cluster/day range is idempotent
- new conversation entry counts are correct
- top trending theme selection and 7-day deltas are correct
- missing trend rows degrade cleanly

### Analytics coverage

- overview cards aggregate correctly from materialized daily rows
- top theme rows return the expected 30-day series
- empty-state behavior works when clusters exist but trends do not

### View-model coverage

- Analytics maps trend cards, momentum labels, and daily bar items correctly
- partial trend data does not break the rest of conversation intelligence

---

## Included

- `ConversationThemeDailyMetricEntity`
- trend materialization service
- Analytics trend overview and per-theme daily series
- focused migration, service, analytics, and view-model tests

## Deferred

- Dashboard surfacing
- arbitrary-range trend queries
- full cluster event history
- chat-side theme trend usage
- manual export/reporting flows for trend intelligence

---

## Recommendation

Proceed with daily theme metric materialization inside Analytics only. It is the cleanest next layer above durable theme clusters and gives Agent-X its first durable long-horizon intelligence surface without overcommitting to a heavier historical event system.
