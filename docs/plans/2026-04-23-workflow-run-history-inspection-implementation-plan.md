# Workflow Run History Inspection Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** existing `WorkflowRunEntity` persistence in `WorkflowEngine`

---

## Goal

Expose recent workflow runs on the existing workflow page and allow users to reopen a historical run in the current result panes.

---

## Scope

### In scope

- recent-run retrieval in `IWorkflowService` / `WorkflowService`
- lightweight run-history models
- selected-workflow history loading in `WorkflowBuilderViewModel`
- recent-runs card in `WorkflowBuilderPage.xaml`
- focused tests

### Out of scope

- schema changes
- run deletion
- run export
- workflow template redesign

---

## Implementation Steps

### 1. Add recent-run inspection models

Files:

- `src/AgentX.Core/Services/Workflows/Models/WorkflowModels.cs`

Work:

- add a compact `WorkflowRunHistoryItem` model
- include parsed step results and computed duration
- keep the model read-only and inspection-focused

### 2. Add service retrieval for recent runs

Files:

- `src/AgentX.Core/Services/Workflows/IWorkflowService.cs`
- `src/AgentX.Core/Services/Workflows/WorkflowService.cs`

Work:

- add a method to fetch recent runs for one workflow
- order by newest first
- parse `StepOutputsJson` safely
- bound the returned list so the page stays lightweight

### 3. Bind recent runs in the workflow view model

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- add recent-run collection and empty-state properties
- load runs when `SelectedWorkflow` changes
- refresh runs after successful or failed execution
- add one command that maps a selected historical run into the existing result properties

### 4. Add the page surface

File:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`

Work:

- add a `Recent Runs` card between the runner card and detailed output cards
- show compact metadata and `Open Result` action
- keep the visual treatment aligned with current workflow cards

### 5. Add focused tests

Files:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`
- add service tests if the repo already has an appropriate workflow-service test location

Work:

- verify selected-workflow change loads recent runs
- verify opening a historical run maps step outputs and final output
- verify running a workflow refreshes recent runs
- verify empty history state is stable

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests|FullyQualifiedName~WorkflowServiceTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- preserve the existing run-progress path
- do not query EF from the view model
- keep history inspection read-only
- keep the first slice scoped to recent runs for the selected workflow only
