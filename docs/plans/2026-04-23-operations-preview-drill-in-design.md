# Operations Preview Drill-In Design

**Date:** 2026-04-23
**Track:** B4 Unified Operations Surface
**Status:** Approved for implementation

## Goal

Make the Operations hub previews actionable so the page can jump directly into the specific inbox item, workflow run, or sync history entry it is previewing, without introducing a new shell-wide navigation parameter system.

## Design

### Navigation pattern

- Reuse the already-proven staged-request pattern used by workflow launch.
- Add a small app-layer `IOperationsDrillInService` that can stage one pending request per target surface:
  - inbox item
  - workflow run
  - sync history entry

### Operations page behavior

- Keep the existing summary cards and preview rows.
- Make `Inbox`, `Workflow run`, and `Sync` preview rows clickable.
- Stage the matching drill-in request before navigating to the owning page.
- Keep `Conversation` and `Connectors` lighter in this pass:
  - conversation previews deep-link to Analytics at page level
  - connector previews deep-link to Plugin Manager at page level

### Target-page behavior

- Inbox:
  - consume pending request on initialization
  - focus the matching item
  - move it to the top of the current list
  - show a small “Opened from Operations” affordance
- Workflow Builder:
  - consume pending workflow-run request
  - select the target workflow
  - reopen the stored run in the existing result surface
  - mark the matching recent-run row
- Sync Settings:
  - consume pending sync-history request
  - move the matching history row to the top
  - mark it as opened from Operations

### Why this approach

- avoids expanding `MainWindow` navigation into ad hoc parameter passing
- keeps the shell router stable
- mirrors an existing pattern already used successfully in the repo
- stays small and testable

### Testing

- add focused tests for:
  - `OperationsDrillInService`
  - `OperationsViewModel` command staging
  - `InboxViewModel` request consumption
  - `WorkflowBuilderViewModel` run-focus consumption
  - `SyncSettingsViewModel` history-focus consumption

## Non-goals

- no generic cross-app navigation parameter framework
- no plugin-detail selection refactor in this pass
- no Analytics summary-row focus plumbing in this pass
