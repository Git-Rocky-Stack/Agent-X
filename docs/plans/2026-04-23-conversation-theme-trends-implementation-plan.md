# Conversation Theme Trends Implementation Plan

**Date:** 2026-04-23  
**Derived from:** `docs/plans/2026-04-23-conversation-theme-trends-design.md`

---

## Task 1: Add durable daily trend entity and EF wiring

**Files**

- Add: `src/AgentX.Core/Data/Entities/ConversationThemeDailyMetricEntity.cs`
- Modify: `src/AgentX.Core/Data/AgentXDbContext.cs`
- Add: migration + snapshot updates under `src/AgentX.Core/Data/Migrations/`

- add the theme daily metrics table
- configure composite key and indexes for trend reads
- keep deletes cascading safely from clusters into daily metric rows

## Task 2: Implement trend materialization service

**Files**

- Add: `src/AgentX.Core/Services/Intelligence/IConversationThemeTrendService.cs`
- Add: `src/AgentX.Core/Services/Intelligence/ConversationThemeTrendService.cs`

- upsert per-cluster daily rows for the trailing 30 days
- keep recomputation idempotent
- expose a bounded refresh for recently touched clusters

## Task 3: Integrate trend refresh with cluster materialization

**Files**

- Modify: `src/AgentX.Core/Services/Intelligence/ConversationThemeClusterService.cs`
- Modify DI wiring in `src/AgentX.App/App.xaml.cs`

- refresh affected cluster trends after cluster recomputation
- keep trend refresh best-effort and non-fatal

## Task 4: Expose trend overview in Analytics contracts

**Files**

- Modify: `src/AgentX.Core/Services/Analytics/Models/AnalyticsModels.cs`
- Modify: `src/AgentX.Core/Services/Analytics/IAnalyticsService.cs`
- Modify: `src/AgentX.Core/Services/Analytics/AnalyticsService.cs`

- add trend overview models and daily series projections
- return top-level cards plus top theme daily series

## Task 5: Surface theme trends in Analytics UI

**Files**

- Modify: `src/AgentX.App/ViewModels/AnalyticsViewModel.cs`
- Modify: `src/AgentX.App/Views/AnalyticsPage.xaml`

- add bounded trend refresh on Analytics load
- map trend cards and per-theme daily bars
- render the new `Theme Trends` block below `Conversation Themes`

## Task 6: Add focused tests

**Files**

- Add: `tests/AgentX.Tests/Services/ConversationThemeTrendServiceTests.cs`
- Add or modify: `tests/AgentX.Tests/Services/AnalyticsServiceConversationThemeTrendTests.cs`
- Modify: `tests/AgentX.Tests/ViewModels/AnalyticsViewModelTests.cs`
- Modify: `tests/AgentX.Tests/Data/MigrationRunner/MigrationRunnerTests.cs`

- verify daily row upsert and idempotency
- verify trend overview aggregation and daily series ordering
- verify Analytics view-model mapping for trend cards and sparkline items
- verify migration runner covers the new table

## Task 7: Verify

- run focused trend/analytics tests
- run WinUI build with `RuntimeIdentifier=win-x64`
- confirm clean tree before push
