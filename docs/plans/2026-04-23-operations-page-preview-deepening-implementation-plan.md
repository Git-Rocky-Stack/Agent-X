# Operations Page Preview Deepening Implementation Plan

**Date:** 2026-04-23
**Design:** `2026-04-23-operations-page-preview-deepening-design.md`
**Track:** B4 Unified Operations Surface

## Steps

1. Extend `OperationsOverviewSnapshot` with preview row models for summaries, sync, inbox, workflows, and connectors.
2. Update `OperationsOverviewService` to populate those collections from existing bounded service calls and analytics queries.
3. Add placeholder preview rows for empty-state clarity.
4. Map the new snapshot collections through `OperationsViewModel`.
5. Render the preview rows in `OperationsPage.xaml` under the existing cards using a shared item template.
6. Extend focused service and view-model tests.
7. Verify with a targeted test pass and a WinUI app build.

## Verification target

- `OperationsOverviewServiceTests`
- `OperationsViewModelTests`
- `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
