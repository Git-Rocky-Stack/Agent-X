# Drill-In Dismiss Parity Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Add explicit dismiss flows for the earlier drill-in landing banners and remove any stale row-focus remnants they leave behind.

## Implementation Steps

1. Inbox

- add a dismiss command to clear the focused inbox landing state
- clear the row-level focused marker when dismissed
- reapply row focus after inbox list reloads while the focused item still exists

2. Workflow Builder

- add a dismiss command to clear the focused workflow-run landing state
- clear the row-level focused marker when dismissed
- reuse the same clear helper when manual user actions move away from the staged Operations focus

3. Validation

- extend focused Inbox and Workflow Builder tests
- run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- run `git diff --check`

## Out of Scope

- redesigning the banner copy
- new drill-in service contracts
- parity changes for destination pages already covered in earlier slices
