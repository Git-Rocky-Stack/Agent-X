# Workflow Result Export Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow run history, portability, and save-to-vault slices

---

## Goal

Add a generic text-artifact export seam and use it to export workflow results from the workflow page.

---

## Scope

### In scope

- `TextArtifactExportItem` model
- `IExportService.ExportTextArtifactAsync`
- text-artifact rendering for markdown, plain text, html, and json
- export dialog/actions for current and stored workflow results
- focused service and view-model tests

### Out of scope

- pdf/docx/pptx workflow-result export
- template-based workflow-result export
- save-file picker integration
- workflow analytics/reporting

---

## Implementation Steps

### 1. Add the core text-artifact export seam

Files:

- `src/AgentX.Core/Services/Export/IExportService.cs`
- `src/AgentX.Core/Services/Export/ExportService.cs`
- `src/AgentX.Core/Services/Export/ExportContentBuilder.cs`
- `src/AgentX.Core/Services/Export/Models/TextArtifactExportItem.cs`

Work:

- add a generic export request model for titled text artifacts with metadata
- add a service method that exports such artifacts to file
- support markdown, plain text, html, and json
- reject unsupported workflow-result export formats cleanly

### 2. Add workflow result export helpers

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- inject `IExportService` as an optional dependency for the linked test path
- add helper methods to export:
  - the current result
  - a stored run result
- normalize workflow result titles and metadata before export

### 3. Surface export actions in the page

Files:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`
- `src/AgentX.App/Views/WorkflowBuilderPage.xaml.cs`

Work:

- add `Export Result` actions to the final-output and stored-run surfaces
- open a small dialog for format selection and metadata toggle
- call into the view model helpers and surface success/failure through status text

### 4. Add focused regression coverage

Files:

- `tests/AgentX.Tests/Services/Export/ExportServiceTests.cs`
- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify text-artifact export writes the expected markdown output
- verify unsupported formats fail cleanly
- verify current-result export sends the expected artifact to the export service
- verify historical-run export does the same

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests|FullyQualifiedName~ExportServiceTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- keep the export-core seam reusable
- keep workflow-result export text-first in this slice
- avoid duplicating export path logic in the page or view model
