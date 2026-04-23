# Conversation Theme Clustering Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-conversation-theme-clustering-design.md`

---

## Task 1: Add durable clustering entities and EF wiring

**Files**

- Modify: `src/AgentX.Core/Data/AgentXDbContext.cs`
- Add: `src/AgentX.Core/Data/Entities/ConversationThemeClusterEntity.cs`
- Add: `src/AgentX.Core/Data/Entities/ConversationThemeMembershipEntity.cs`
- Modify: `src/AgentX.Core/Data/Entities/ConversationSummarySnapshotEntity.cs`
- Add: migration + snapshot updates under `src/AgentX.Core/Data/Migrations/`

- add cluster and membership tables
- extend summary snapshots with clustering embedding fields
- configure relationships and indexes for latest-snapshot materialization lookups

## Task 2: Implement theme clustering service

**Files**

- Add: `src/AgentX.Core/Services/Intelligence/ConversationThemeClusterService.cs`
- Add: `src/AgentX.Core/Services/Intelligence/IConversationThemeClusterService.cs`
- Add or modify supporting models under `src/AgentX.Core/Services/Intelligence/Models/`

- ensure snapshot embeddings for latest summaries
- assign latest snapshots into existing or new clusters
- update memberships idempotently
- recompute cluster aggregate fields after assignment

## Task 3: Integrate bounded refresh behavior

**Files**

- Modify: `src/AgentX.Core/Services/Chat/ConversationSummaryService.cs`
- Modify: `src/AgentX.Core/Services/Analytics/AnalyticsService.cs`
- Modify DI registration in `src/AgentX.App/App.xaml.cs`

- trigger or mark clustering work after summary refresh
- allow Analytics to refresh a bounded number of stale cluster assignments
- keep clustering non-fatal for summary persistence and Analytics reads

## Task 4: Expose theme overview in Analytics contracts

**Files**

- Modify: `src/AgentX.Core/Services/Analytics/Models/AnalyticsModels.cs`
- Modify: `src/AgentX.Core/Services/Analytics/IAnalyticsService.cs`
- Modify: `src/AgentX.Core/Services/Analytics/AnalyticsService.cs`

- add theme overview and cluster row projections
- expose counts such as active clusters, clustered conversations, new themes, and last materialized timestamp
- return recent/materialized cluster rows for the Analytics surface

## Task 5: Surface conversation themes in Analytics UI

**Files**

- Modify: `src/AgentX.App/ViewModels/AnalyticsViewModel.cs`
- Modify: `src/AgentX.App/Views/AnalyticsPage.xaml`

- map theme overview cards into the view model
- add repeater-friendly cluster display items
- render the new `Conversation Themes` section below conversation intelligence

## Task 6: Add focused tests

**Files**

- Add/modify: `tests/AgentX.Tests/Services/ConversationThemeClusterServiceTests.cs`
- Modify: `tests/AgentX.Tests/Services/AnalyticsServiceConversationIntelligenceTests.cs`
- Modify: `tests/AgentX.Tests/ViewModels/AnalyticsViewModelTests.cs`
- Modify: `tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs`

- verify cluster assignment, new-cluster creation, and reassignment
- verify theme overview aggregation and empty states
- verify Analytics view-model mapping for cards and cluster rows
- verify migration runner picks up the new schema change

## Task 7: Verify

- run focused clustering/analytics tests
- run WinUI build with `RuntimeIdentifier=win-x64`
- confirm clean tree before push
