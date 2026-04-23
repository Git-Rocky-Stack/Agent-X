# Workflow Result Save-To-Vault Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow run history inspection and workflow portability slices

---

## Goal

Connect workflow outputs to the Knowledge Vault by saving live and historical results as imported documents.

---

## Scope

### In scope

- save-current-result command
- save-historical-run command
- temp file staging for workflow-result documents
- Knowledge Vault navigation command
- result-surface action buttons
- focused view-model tests

### Out of scope

- generic export-core changes
- collection assignment
- post-save document auto-selection in the vault
- workflow-result schema extensions

---

## Implementation Steps

### 1. Add workflow-result vault handoff behavior

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- inject `IDocumentService` and `IAppNavigationService`
- add commands for:
  - saving the current result
  - saving a historical run
  - opening the Knowledge Vault
- stage saved result files under the app temp tree
- write a short provenance header before the result body
- import through `ImportExternalContentAsync`

### 2. Surface result actions on the page

File:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`

Work:

- add `Save as Document` to the final output card
- add `Open Vault` to the final output card
- add `Save as Document` to each recent run item
- keep the visual treatment aligned with existing workflow action buttons

### 3. Add focused coverage

File:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify saving the current result imports a workflow-result document
- verify saving a historical run imports the expected content
- verify `Open Vault` delegates navigation correctly

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- keep the saved document format plain text
- keep result handoff view-model owned and testable
- do not expand the export system in this slice
