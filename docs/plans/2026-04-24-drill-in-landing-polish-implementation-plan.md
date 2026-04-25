# Drill-In Landing Polish Implementation Plan

Date: 2026-04-24
Repo: Agent-X

## Objective

Strengthen the post-navigation confirmation state for recommendation-driven drill-ins and widen vault filters when needed so the requested target is actually visible.

## Implementation Steps

1. Inbox

- Add focused landing-state properties to the viewmodel
- Surface a top-level landing banner above the inbox list
- Preserve the existing row-level focus marker

2. Knowledge Vault

- Add a focused landing hint for the selected document preview
- Widen filters when a staged document is hidden by the current filter state
- Surface a preview-panel landing banner above the selected document metadata

3. Plugin Manager

- Upgrade the selected-plugin operations badge into a more prominent detail callout

4. Workflow Builder

- Track focused workflow-run landing state in the viewmodel
- Surface a runner-section landing banner when a stored run was opened through drill-in

5. Validation

- Extend existing Inbox, Knowledge Vault, and Workflow Builder tests
- Run `dotnet build tests\\AgentX.Tests\\AgentX.Tests.csproj --no-restore`
- Run `git diff --check`

## Out of Scope

- New navigation infrastructure
- New destination-page commands
- Cross-page animation work
- Non-drill-in page redesign
