# Workflow Empty State Starters Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow template actions and template guidance slices

---

## Goal

Replace the workflow page's simple empty state with a guided starter panel that highlights built-in templates and lets users select one directly.

---

## Scope

### In scope

- starter-template display state in `WorkflowBuilderViewModel`
- `Select Template` command
- upgraded empty-state panel in `WorkflowBuilderPage.xaml`
- focused tests

### Out of scope

- service changes
- new persistence
- direct template cloning from the empty state

---

## Implementation Steps

### 1. Add starter state to the view model

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- expose built-in starter cards derived from `Workflows`
- add visibility helpers for the no-selection empty state
- add a `SelectTemplate` command that sets `SelectedWorkflow`

### 2. Replace the passive empty state

File:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`

Work:

- replace the current icon/text placeholder
- add plain-language empty-state copy
- add a compact repeater of starter cards with `Select Template`
- add a secondary `Create Blank Workflow` button

### 3. Add focused tests

File:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify built-in workflows appear in the starter list
- verify selecting a starter sets `SelectedWorkflow`
- verify custom workflows are not shown as starter cards

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- keep the empty-state slice lightweight
- reuse the built-in workflows already loaded into the page
- route starter selection into the existing runner/template-guide flow
