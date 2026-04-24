# Operations Connector Enable Action Implementation Plan

**Date:** 2026-04-24  
**Depends on:** current local Operations remediation and drill-in surface

## Steps

1. Extend `OperationsConnectorPreview` with explicit enable-state flags.
2. Update `OperationsOverviewService.BuildConnectorPreviews(...)` to mark disabled connectors as Operations-enableable.
3. Add `EnableConnectorAsync(long pluginId)` to `IOperationsActionService`.
4. Implement connector enable orchestration in `OperationsActionService` via `IPluginService`.
5. Update `OperationsViewModel` with:
   - connector-enable command
   - busy-state gating
   - post-action snapshot reload
6. Update `OperationsPage.xaml` connector preview rows so:
   - disabled connectors show `Enable Connector`
   - all previews still offer `Open Plugin`
7. Add focused regression coverage for:
   - `OperationsActionServiceTests`
   - `OperationsOverviewServiceTests`
   - `OperationsViewModelTests`
8. Verify with:
   - focused `dotnet test` for operations action/overview/viewmodel coverage
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
