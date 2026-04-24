# Operations Overview Unification Implementation Plan

Date: 2026-04-23

## Objective

Implement the first `B4` slice by introducing a shared operations overview seam and using it to upgrade the Dashboard operations section.

## Tasks

1. Add app-layer operations overview models and service.
   - Create a snapshot model for the dashboard operations cards.
   - Implement `OperationsOverviewService` using analytics, inbox, sync, workflow, and plugin sources.

2. Register the service in app DI.
   - Add the new service to `App.xaml.cs`.

3. Simplify dashboard operations loading.
   - Replace the current direct stitching logic in `DashboardViewModel` with a single operations snapshot load.
   - Add connector/plugin properties and navigation for Plugin Manager.

4. Update the dashboard operations layout.
   - Reframe Smart Inbox as ingestion backlog.
   - Add a connector/plugin card.
   - Keep workflow intelligence compact but visible.

5. Add focused tests.
   - Add an `OperationsOverviewService` test for connector, backlog, and workflow snapshot mapping.
   - Update `DashboardViewModelTests` for the new operations snapshot path and plugin-manager navigation.

6. Verify.
   - Run focused operations/dashboard tests.
   - Run `dotnet build` for `AgentX.App` with `RuntimeIdentifier=win-x64`.
