# Workflow Template Actions Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow run history inspection slice

---

## Goal

Expose actionable template/custom workflow controls in the workflow list and clone built-ins into editable workflows through a dedicated service seam.

---

## Scope

### In scope

- workflow clone method in `IWorkflowService` / `WorkflowService`
- `UseTemplateAsync` command in `WorkflowBuilderViewModel`
- list-item `Edit` / `Delete` / `Use Template` actions
- richer workflow description display
- focused tests

### Out of scope

- template marketplace
- workflow analytics
- advanced template versioning

---

## Implementation Steps

### 1. Add a workflow cloning service seam

Files:

- `src/AgentX.Core/Services/Workflows/IWorkflowService.cs`
- `src/AgentX.Core/Services/Workflows/WorkflowService.cs`

Work:

- add one method that clones a source workflow and its steps into a new non-built-in workflow
- preserve icon, category, description, and step configuration
- default the cloned name to a readable copy name

### 2. Add template/custom actions to the view model

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- add a `UseTemplateAsync` command
- after cloning, refresh workflows and open the copied workflow in the existing editor
- expose helper properties for list-item display where needed

### 3. Upgrade the workflow list UI

File:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`

Work:

- show description preview in the left list
- show a `Template` badge for built-ins
- show `Use Template` for built-ins
- show `Edit` and `Delete` for custom workflows

### 4. Add focused tests

Files:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`
- `tests/AgentX.Tests/Services/WorkflowServiceTests.cs`

Work:

- verify cloning copies steps and clears built-in state
- verify `UseTemplateAsync` opens the copied workflow in editor state
- verify list-action view-model flows remain stable

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests|FullyQualifiedName~WorkflowServiceTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- preserve direct workflow execution
- keep the clone path service-owned
- avoid introducing a second editor or template-specific page
