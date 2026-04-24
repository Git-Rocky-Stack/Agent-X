# Workflow Analytics Implementation Plan

Date: 2026-04-23

## Objective

Implement the first workflow analytics surface in Analytics without changing Dashboard or adding new persistence.

## Tasks

1. Extend analytics models and service contracts.
   - Add workflow overview and row projection models in `AnalyticsModels.cs`.
   - Add workflow overview and daily trend methods to `IAnalyticsService`.

2. Implement workflow analytics queries in `AnalyticsService`.
   - Aggregate run totals, success/failure counts, success rate, average duration, and recently active workflow count.
   - Build top-workflow rows from `WorkflowRuns` joined to `Workflows`.
   - Build recent-run rows with compact preview text and duration metadata.
   - Add daily workflow run metrics using the existing gap-fill pattern.

3. Surface workflow analytics in `AnalyticsViewModel`.
   - Add summary card properties, trend items, top-workflow items, recent-run items, and empty-state flags.
   - Load the new workflow section inside the existing Analytics load path with isolated failure handling.

4. Add the Analytics UI section.
   - Insert a `Workflow Intelligence` block into `AnalyticsPage`.
   - Add summary cards, a compact trend strip, and side-by-side `Top Workflows` / `Recent Workflow Runs` lists.
   - Keep layout and styles aligned with the current Analytics surface.

5. Add focused tests.
   - Add analytics service tests for summary aggregation, ordering, success rate, and gap-filled daily trends.
   - Extend analytics view-model tests for populated and empty workflow states.

6. Verify.
   - Run the focused analytics test slice.
   - Run `dotnet build` for `AgentX.App` with `RuntimeIdentifier=win-x64`.

## Out Of Scope

- Dashboard mirroring
- workflow authoring changes
- new workflow persistence or migrations
- historical export/reporting changes
