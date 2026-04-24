# Operations Remediation Actions Implementation Plan

**Date:** 2026-04-24  
**Depends on:** current local Operations page + drill-in work

## Steps

1. Add `IOperationsActionService` and `OperationsActionService`.
2. Implement `RefreshConversationSummariesAsync` using `IConversationSummaryService.RefreshStaleSummariesAsync`.
3. Implement `RunManualSyncAsync` using the existing bounded export/import pattern already used by Sync Settings.
4. Register the new service in `App.xaml.cs`.
5. Update `OperationsViewModel` with:
   - command state for refresh/sync actions
   - success/error action feedback
   - post-action snapshot reload
6. Update `OperationsPage.xaml` with:
   - `Refresh Summaries` button on the conversation card
   - `Sync Now` button on the sync card
   - compact action feedback strip
7. Add focused regression coverage for:
   - `OperationsActionServiceTests`
   - `OperationsViewModelTests`
8. Verify with:
   - focused `dotnet test` for operations action/viewmodel coverage
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
