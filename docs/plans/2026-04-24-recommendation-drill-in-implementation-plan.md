# Recommendation Drill-In Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Carry exact record IDs through recommendation items and stage those drill-ins before navigation on Dashboard, Operations, and Quick Actions.

## Implementation Steps

1. Dashboard

- Store the full `OperationsOverviewSnapshot`
- Extend `DashboardRecommendedActionItem` with target metadata
- Prefer exact document, inbox, connector, and workflow-run targets when previews exist
- Stage drill-in requests before `NavigateRequested`

2. Operations

- Extend `OperationsRecommendedActionItem` with secondary target metadata for workflow runs
- Route recommendation navigation through a single helper that stages drill-ins first
- Preserve current direct-action behavior for reindex, enable connector, preview generation, and sync refresh

3. Quick Actions

- Inject the existing drill-in service
- Extend contextual recommendation items with target metadata where needed
- Stage inbox, vault, and plugin drill-ins before navigation

4. Validation

- Add focused tests for:
  - dashboard targeted recommendation metadata
  - dashboard workflow-run staging
  - quick-actions inbox staging
  - quick-actions vault staging
  - operations workflow-run staging
- Run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- Run `git diff --check`

## Out of Scope

- New destination-page layouts
- New navigation infrastructure
- Remediation beyond existing action services
- Cross-page deep links for non-recommendation surfaces
