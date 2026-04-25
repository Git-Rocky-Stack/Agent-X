# Analytics And Sync Action Resolution Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Add explicit action-resolution behavior to Analytics and Sync Settings without changing the existing Operations drill-in contract.

## Implementation Steps

1. Analytics

- add a focused conversation-summary refresh command to `AnalyticsViewModel`
- refresh the exact staged conversation via `IConversationSummaryService.RefreshConversationSummaryAsync`
- on success, clear the focused landing state, reload conversation intelligence, and show a resolved confirmation
- surface the focused refresh action and the resulting status line in `AnalyticsPage.xaml`

2. Sync Settings

- detect whether a focused sync-history landing is active when `SyncNowAsync` starts
- after a successful sync and history reload, clear the focused landing state and replace it with a resolved status message
- preserve the existing generic success message when no focused sync drill-in is active

3. Validation

- extend `AnalyticsViewModelTests` for focused-summary resolution
- extend `SyncSettingsViewModelTests` for focused sync-history resolution
- run `git diff --check`
- run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- run a focused `dotnet test` filter for analytics/sync view-model coverage if the environment allows it

## Out Of Scope

- new cross-page navigation infrastructure
- redesigning the Analytics or Sync page layouts
- automatic resolution for passive refreshes that do not correspond to a user action
