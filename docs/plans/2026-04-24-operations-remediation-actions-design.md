# Operations Remediation Actions Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** add safe Operations-side remediation actions instead of leaving the hub as navigation-only chrome

## Goal

Turn the Operations hub into a lightweight control surface, not just a routing page. The first pass should expose only actions that are already safe, bounded, and meaningful from the top-level operations context.

## Chosen scope

Two actions:

- `Refresh Summaries` on the `Conversation Intelligence` card
- `Sync Now` on the `Sync Health` card

Both actions should execute from the Operations page, then refresh the Operations snapshot so the cards reflect the updated state.

## Architecture

Use a small shared app-layer service rather than putting remediation logic directly into `OperationsViewModel`.

- add `IOperationsActionService` / `OperationsActionService`
- `Refresh Summaries` calls `IConversationSummaryService.RefreshStaleSummariesAsync`
- `Sync Now` reuses the existing manual-sync behavior shape already used in Sync Settings:
  export local changes, run a bounded import pass via `StartAutoSyncAsync`, then return a friendly result message
- `OperationsViewModel` owns command state, action feedback, and post-action snapshot reload

## UX

- place the new buttons directly beside the existing `Open Analytics` / `Open Sync` affordances on the matching cards
- add a compact success/error strip near the top of the page so users get confirmation without leaving Operations
- keep action count intentionally low for this slice; no destructive inbox batch actions or plugin toggles from Operations yet

## Deferred

- additional remediation actions for inbox or workflows
- richer progress UI per action
- deduplicating the manual-sync logic with Sync Settings into a broader shared orchestration layer
