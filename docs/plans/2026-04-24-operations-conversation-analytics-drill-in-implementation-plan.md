# Operations Conversation Analytics Drill-In Implementation Plan

**Date:** 2026-04-24  
**Depends on:** existing Operations preview drill-in slice

## Steps

1. Extend `OperationsConversationPreview` with `ConversationId` and populate it from `OperationsOverviewService`.
2. Extend `IOperationsDrillInService` / `OperationsDrillInService` with a one-shot conversation drill-in request.
3. Update `OperationsViewModel.OpenConversationPreview` to stage the conversation request before navigating to `Analytics`.
4. Update `AnalyticsViewModel` to consume the pending request during recent-summary mapping, promote the matching summary to the top, and attach a visible source label.
5. Update `AnalyticsPage.xaml` to surface the source label as an accent badge on the focused summary card.
6. Extend focused tests for:
   - `OperationsDrillInServiceTests`
   - `OperationsViewModelTests`
   - `AnalyticsViewModelTests`
7. Verify with:
   - focused `dotnet test` for operations/analytics drill-in coverage
   - `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
