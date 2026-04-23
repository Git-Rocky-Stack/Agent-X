# Workflow Portability Actions Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow template actions and workflow starter/template guidance slices

---

## Goal

Surface clipboard-first workflow import/export actions on the workflow page by reusing the existing service-layer JSON seams.

---

## Scope

### In scope

- import action in the workflow list panel
- export action in the selected-workflow runner section
- clipboard-prefilled import dialog
- export helper seam in the workflow view model
- imported-workflow selection after successful import
- focused tests

### Out of scope

- file-picker integration
- remote sharing
- bulk import/export
- workflow packaging/versioning

---

## Implementation Steps

### 1. Add portability helper behavior to the view model

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- add one helper method to retrieve export JSON through the service layer
- keep import in the view model and select the imported workflow after refresh
- preserve existing status messaging behavior

### 2. Surface the actions in the workflow page

Files:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`
- `src/AgentX.App/Views/WorkflowBuilderPage.xaml.cs`

Work:

- add `Import Workflow` beside `New Workflow`
- add `Export JSON` in the selected-workflow runner header
- open a multiline import dialog on click
- prefill the dialog from clipboard text when available
- copy exported JSON to the clipboard from the page layer

### 3. Add focused regression coverage

File:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify import selects the new workflow after reload
- verify the export helper returns the serialized workflow JSON

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- avoid introducing a second workflow-management surface
- keep the current JSON schema untouched
- keep clipboard concerns in the page layer where practical
