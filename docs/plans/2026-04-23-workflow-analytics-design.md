# Workflow Analytics Design

Date: 2026-04-23

## Goal

Add an Analytics-only workflow intelligence surface that makes workflow adoption, reliability, and recent results visible without adding new persistence or changing Dashboard in the same pass.

## Scope

- Extend `IAnalyticsService` with workflow analytics queries.
- Surface workflow intelligence inside `AnalyticsPage`.
- Keep the feature read-only and passive.
- Reuse existing `WorkflowRuns` and `Workflows` data only.

## Approved Approach

The first slice stays Analytics-only.

Reasoning:

- Analytics is already the established inspection surface for durable intelligence.
- Workflow maturity needs better visibility, but Dashboard should not absorb another detailed module before the workflow data story is proven.
- This keeps the slice narrow and avoids duplicating workflow-page concerns.

## Data Model

The new analytics layer should expose:

- total workflow runs
- successful runs
- failed or cancelled runs
- success rate
- average run duration
- active workflows used recently
- top workflows by run count
- recent runs for inspection
- daily workflow run metrics for a bounded trend strip

Constraints:

- Top workflows must be derived from `WorkflowRuns` joined to `Workflows`.
- The slice must not trust `WorkflowEntity.RunCount` as the primary source.
- Recent runs should use a compact analytics projection rather than the richer workflow-runner model.
- No migrations or new persistence objects are needed.

## UI Surface

Add a dedicated `Workflow Intelligence` section to `AnalyticsPage`.

The section should include:

- summary cards for total runs, success rate, average run duration, and active workflows used recently
- a compact 30-day workflow-run trend strip
- a `Top Workflows` list with run volume and success-rate labels
- a `Recent Workflow Runs` list with workflow name, status, started-at, duration, and a short final-output or error preview

The visual treatment should match the current Analytics language and stay below the existing top summary/indexing surfaces.

## Empty State And Failure Behavior

- If there are no workflow runs yet, show a clear empty state instead of a blank section.
- If workflow analytics loading fails, degrade only that section.
- The rest of Analytics must continue to load normally.

## Testing

Add focused coverage for:

- workflow summary aggregation
- daily workflow trend gap filling
- top-workflow ordering and success-rate calculation
- populated workflow analytics view-model mapping
- empty workflow analytics state handling

Verification should be a focused analytics test slice plus a WinUI build.
