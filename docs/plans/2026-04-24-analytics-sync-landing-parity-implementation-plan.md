# Analytics And Sync Landing Parity Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Add focused landing banners and dismiss flows for Analytics conversation summaries and Sync history entries while preserving exact-record drill-in behavior across reloads.

## Implementation Steps

1. Analytics

- add focused landing properties to `AnalyticsViewModel`
- persist the focused conversation summary across reloads until dismissed
- surface a focused-summary banner in the Analytics page
- keep the focused summary visually marked in the row template

2. Sync Settings

- add focused landing properties to `SyncSettingsViewModel`
- reapply focused sync history state whenever history reloads
- surface a focused-history banner in the Sync Settings page
- add a dismiss command that clears the focus state and only clears the status strip when it still represents the drill-in

3. Validation

- extend focused Analytics and Sync Settings tests
- run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- run `git diff --check`

## Out of Scope

- landing-banner dismiss parity for every previously updated destination
- new navigation infrastructure
- changes to the Operations drill-in service contract
