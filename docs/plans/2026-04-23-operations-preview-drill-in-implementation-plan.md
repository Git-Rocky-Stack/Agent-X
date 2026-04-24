# Operations Preview Drill-In Implementation Plan

**Date:** 2026-04-23
**Design:** `2026-04-23-operations-preview-drill-in-design.md`
**Track:** B4 Unified Operations Surface

## Steps

1. Add `IOperationsDrillInService` and staged request records for inbox, workflow runs, and sync history.
2. Register the service in app DI.
3. Extend Operations preview models with the IDs needed to target the correct records.
4. Add Operations page commands that stage requests and navigate.
5. Make the relevant Operations preview rows clickable.
6. Update Inbox to consume and focus a pending item request.
7. Update Workflow Builder to consume and reopen a pending stored-run request.
8. Update Sync Settings to consume and focus a pending history request.
9. Add lightweight “Opened from Operations” affordances on the focused target rows.
10. Add focused tests and verify with a targeted test slice plus a WinUI build.

## Verification target

- `OperationsDrillInServiceTests`
- `OperationsViewModelTests`
- `InboxViewModelTests`
- `WorkflowBuilderViewModelTests`
- `SyncSettingsViewModelTests`
- `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
