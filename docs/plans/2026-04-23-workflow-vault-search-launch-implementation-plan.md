# Workflow Vault Search Launch Implementation Plan

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Depends on:** workflow run history, template actions, save-to-vault, result export

---

## Goal

Connect the main knowledge surfaces to workflows so users can launch a workflow directly from a document or search result.

---

## Scope

### In scope

- staged workflow launch service
- vault document -> workflow handoff
- search result -> workflow handoff
- workflow input prefill and starter-template selection
- focused view-model tests

### Out of scope

- auto-run on navigation
- batch handoff from multi-select
- generalized navigation parameters across the whole app

---

## Implementation Steps

### 1. Add the staged launch seam

Files:

- `src/AgentX.App/Services/IWorkflowLaunchService.cs`
- `src/AgentX.App/Services/WorkflowLaunchRequest.cs`
- `src/AgentX.App/Services/WorkflowLaunchService.cs`
- `src/AgentX.App/App.xaml.cs`

Work:

- add a single pending-request app service
- register it as a singleton
- keep the API narrow: stage + consume

### 2. Wire Knowledge Vault handoff

Files:

- `src/AgentX.App/ViewModels/KnowledgeVaultViewModel.cs`
- `src/AgentX.App/Views/KnowledgeVaultPage.xaml`
- `src/AgentX.App/Views/KnowledgeVaultPage.xaml.cs`
- `src/AgentX.Core/Documents/IDocumentService.cs`
- `src/AgentX.Core/Documents/DocumentService.cs`

Work:

- add a testable launch command in the view model
- fetch a lightweight document preview through the document service
- stage a structured request and navigate to `Workflows`
- add a `Workflow` button on document cards

### 3. Wire Search handoff

Files:

- `src/AgentX.App/ViewModels/SearchViewModel.cs`
- `src/AgentX.App/Views/SearchPage.xaml`
- `src/AgentX.App/Views/SearchPage.xaml.cs`

Work:

- add a testable launch command for a search result
- stage the current query plus excerpt as workflow input
- add a `Workflow` button beside the existing `Open` action

### 4. Consume staged input in Workflow Builder

Files:

- `src/AgentX.App/ViewModels/WorkflowBuilderViewModel.cs`

Work:

- inject the launch service as an optional trailing dependency
- consume the pending request during initialization
- prefill `RunInput`
- clear stale execution state
- select the recommended starter template when present

### 5. Add focused regression coverage

Files:

- `tests/AgentX.Tests/AgentX.Tests.csproj`
- `tests/AgentX.Tests/ViewModels/KnowledgeVaultViewModelTests.cs`
- `tests/AgentX.Tests/ViewModels/SearchViewModelTests.cs`
- `tests/AgentX.Tests/ViewModels/WorkflowBuilderViewModelTests.cs`

Work:

- verify vault document launch stages a request and navigates
- verify search result launch stages a request and navigates
- verify workflow builder consumes the staged request correctly

---

## Verification

Run:

- `dotnet test tests\\AgentX.Tests\\AgentX.Tests.csproj --filter "FullyQualifiedName~KnowledgeVaultViewModelTests|FullyQualifiedName~SearchViewModelTests|FullyQualifiedName~WorkflowBuilderViewModelTests" --no-restore`
- `dotnet build src\\AgentX.App\\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`

---

## Guardrails

- keep the launch seam app-local and intentionally narrow
- do not add a broad parameterized-navigation system in this slice
- do not auto-run workflows from imported source payloads
