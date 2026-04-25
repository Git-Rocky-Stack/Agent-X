# Vault And Plugin Drill-In Lifecycle Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Close the remaining drill-in lifecycle gaps on Knowledge Vault and Plugin Manager without changing their overall page structures.

## Implementation Steps

1. Knowledge Vault

- add a dismiss command for focused document landing state
- clear row-level focused source labels alongside the banner
- clear lingering focus when the user selects another document or closes the preview

2. Plugin Manager

- preserve focused connector state across `LoadPluginsAsync` refreshes
- add a dismiss command that clears focused row state and restores the default footer message
- wire the detail-panel callout to that dismiss command
- clear focus when the user intentionally selects a different connector

3. Validation

- extend Knowledge Vault and Plugin Manager tests
- run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- run `git diff --check`

## Out Of Scope

- changing the Operations drill-in contracts
- redesigning the destination page layouts
- new recommendation types
