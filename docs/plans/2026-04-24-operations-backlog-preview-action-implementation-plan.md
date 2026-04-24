# Operations Backlog Preview Action Implementation Plan

**Date:** 2026-04-24  
**Depends on:** current local Operations remediation-action slice

## Steps

1. Extend `IOperationsActionService` with `GenerateInboxPreviewsAsync`.
2. Implement that method in `OperationsActionService` using `IInboxService.GetPendingCountAsync` and `GenerateAllPreviewsAsync`.
3. Update `OperationsViewModel` with:
   - `IsGeneratingInboxPreviews`
   - `GenerateInboxPreviewsCommand`
   - command-state updates tied to loading state and backlog headline
   - shared feedback and snapshot reload after completion
4. Update `OperationsPage.xaml` to add `Generate Previews` beside `Open Inbox` on the backlog card.
5. Extend focused tests for:
   - `OperationsActionServiceTests`
   - `OperationsViewModelTests`
6. Verify with:
   - focused `dotnet test` for operations action/viewmodel coverage
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
