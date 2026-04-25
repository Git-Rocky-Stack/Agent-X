# Drill-In Action Resolution Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Close the final drill-in review-lifecycle gap by clearing focused destination state when the requested remediation action succeeds.

## Implementation Steps

1. Inbox

- detect when the accepted item matches the active focused inbox drill-in target
- clear the focused inbox landing state after a successful accept
- swap the source-text banner state for a resolved confirmation message

2. Plugin Manager

- detect when direct enable targets the focused connector
- detect when bulk enable includes the focused connector
- clear focused connector state and replace the source-text status with a resolved confirmation

3. Workflow Builder

- resolve focused stored-run state after save-to-vault success for reopened current-result context
- resolve focused stored-run state after save-to-vault success for row-based historical-run actions
- resolve focused stored-run state after export success for reopened current-result context
- resolve focused stored-run state after export success for row-based historical-run actions

4. Validation

- extend Inbox, Plugin Manager, and Workflow Builder tests for the new resolution behavior
- run `git diff --check`
- run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- run a focused `dotnet test` filter for the touched view-model suites if the environment allows it

## Out Of Scope

- adding new Operations drill-in types
- redesigning banner visuals or destination layouts
- automatic resolution for non-remediation navigation events
