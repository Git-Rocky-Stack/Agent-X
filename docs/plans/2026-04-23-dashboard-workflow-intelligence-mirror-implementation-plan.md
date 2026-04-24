# Dashboard Workflow Intelligence Mirror Implementation Plan

Date: 2026-04-23

## Objective

Upgrade the Dashboard workflow operations card to reuse the new workflow analytics overview.

## Tasks

1. Update dashboard workflow data loading.
   - Request workflow intelligence overview inside `LoadOperationsOverviewAsync`.
   - Keep the dashboard load parallelized with the other operations overview calls.

2. Rework the workflow card mapping.
   - Derive headline, success-rate status, recent-activity badge text, average-duration badge text, and top-workflow detail from the analytics overview.
   - Preserve a clear empty state when no workflow runs exist yet.

3. Lightly update the dashboard card UI.
   - Add compact supporting labels or badges for recent activity and average duration.
   - Keep the layout within the existing workflow card footprint.

4. Extend tests.
   - Update `DashboardViewModelTests` for the new workflow intelligence mapping.
   - Keep existing operations overview and navigation coverage passing.

5. Verify.
   - Run focused `DashboardViewModelTests`.
   - Run `dotnet build` for `AgentX.App` with `RuntimeIdentifier=win-x64`.
