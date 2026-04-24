# Operations Imported Documents Vault Handoff Implementation Plan

**Date:** 2026-04-24  
**Depends on:** current local Operations remediation, preview, and drill-in infrastructure

## Steps

1. Extend `OperationsOverviewSnapshot` with a recent imported-document preview collection.
2. Add `OperationsImportedDocumentPreview` carrying `DocumentId`.
3. Update `OperationsOverviewService` to query accepted inbox items and map those with a bridged `DocumentId` into recent import previews.
4. Extend `IOperationsDrillInService` / `OperationsDrillInService` with a one-shot document drill-in request.
5. Update `OperationsViewModel` with:
   - recent imported-document state mapping
   - `OpenImportedDocumentPreview`
   - `NavigateToKnowledgeVault`
6. Update `OperationsPage.xaml` to:
   - render `Recent imported documents` inside the backlog card
   - add an `Open Vault` button
7. Update `KnowledgeVaultViewModel` to consume the pending document request after load, promote the matching document, and select it.
8. Add small focused affordances in `KnowledgeVaultPage.xaml` so the promoted document shows it was opened from Operations.
9. Extend focused regression coverage for:
   - `OperationsOverviewServiceTests`
   - `OperationsDrillInServiceTests`
   - `OperationsViewModelTests`
   - `KnowledgeVaultViewModelTests`
10. Verify with:
   - focused `dotnet test` for operations + vault drill-in coverage
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
