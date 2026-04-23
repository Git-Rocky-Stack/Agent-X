# Workflow Template Guidance Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** template actions slice

---

## Goal

Show a lightweight guidance card for selected built-in workflow templates so non-technical users understand when and how to use them.

---

## Scope

### In scope

- static guide catalog in `WorkflowBuilderViewModel`
- selected-template guidance properties
- `Template Guide` card in `WorkflowBuilderPage.xaml`
- focused tests

### Out of scope

- service-layer changes
- persistence changes
- generated help text

---

## Implementation Steps

### 1. Add guide display state

File:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- add a static guide catalog for the built-in templates
- expose selected-template guide properties and visibility flags
- map built-in workflow names to summary, best-for text, outcome text, and starter examples

### 2. Add the page surface

File:

- `src/AgentX.App/Views/WorkflowBuilderPage.xaml`

Work:

- add a `Template Guide` block at the top of the runner section
- bind it only when a built-in template with guide content is selected
- keep styling aligned with the current workflow cards

### 3. Add focused tests

File:

- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify built-in selection exposes guide state
- verify custom workflow selection hides guide state
- verify starter examples are populated for known templates

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~WorkflowBuilderViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- do not change workflow execution behavior
- do not auto-populate inputs
- keep the guide content deterministic and concise
