# Operations Connector Drill-In Implementation Plan

**Date:** 2026-04-24  
**Depends on:** existing Operations preview drill-in slice

## Steps

1. Extend `OperationsConnectorPreview` with the plugin entity ID and populate it from `OperationsOverviewService`.
2. Add `OperationsPluginDrillInRequest` plus stage/consume methods to `IOperationsDrillInService` and `OperationsDrillInService`.
3. Update `OperationsViewModel.OpenConnectorPreview` to stage the plugin request before navigating to `PluginManager`.
4. Update `PluginManagerViewModel` to consume pending plugin requests, mark the target plugin focused, move it to the top, and expose the source label.
5. Update `PluginManagerPage` to auto-select the focused plugin and show “Opened from Operations” badges in the list and detail surfaces.
6. Add or extend focused regression coverage for:
   - `OperationsDrillInServiceTests`
   - `OperationsViewModelTests`
   - `PluginManagerViewModelTests`
7. Verify with:
   - focused `dotnet test` for operations/plugin drill-in tests
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
