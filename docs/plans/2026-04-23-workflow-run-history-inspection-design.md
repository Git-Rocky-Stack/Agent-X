# Workflow Run History Inspection Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Make workflow execution feel persistent and inspectable by exposing recent run history directly on the existing workflow page.

---

## Problem

Workflow runs are already persisted in `WorkflowRunEntity`, including status, timestamps, token totals, final output, and serialized step outputs. The current UI only shows the active in-memory run. Once the user navigates away or runs something else, the previous result is effectively hidden.

That makes workflows feel transient even though the backend already records durable execution history.

---

## Recommended Approach

Add a lightweight recent-runs surface to `WorkflowBuilderPage` and let users open a historical run into the existing step-output and final-output panes.

This keeps the slice small and coherent:

- no new page
- no new persistence schema
- no second result viewer
- no workflow redesign

The page simply gains one durable seam: `recent runs for the selected workflow`.

---

## Scope

### In scope

- service-level retrieval for recent runs by workflow
- parsing persisted step-output JSON into inspectable models
- a recent-runs list on the workflow page
- a command to open a historical run into the existing result panes
- automatic refresh of recent runs after a new run completes
- focused service/view-model coverage

### Out of scope

- workflow template expansion
- workflow analytics dashboards
- run deletion/export
- cross-workflow run search
- new database migrations

---

## UI Shape

Keep the current page structure and add a new `Recent Runs` card between the runner section and the detailed output panes.

Each run item should show:

- status
- relative recency or timestamp
- steps completed vs total
- token total
- duration when available

Each item gets one primary action:

- `Open Result`

Selecting a historical run should repopulate the existing step outputs and final output panes. Those panes remain the single place where detailed workflow output is read.

---

## Data Flow

1. Selecting a workflow loads recent run history for that workflow.
2. Running a workflow still populates the current in-memory progress state.
3. When the run finishes, the recent-runs list refreshes.
4. Opening a historical run maps persisted run data into the same display properties already used by the current result panes.

This preserves one result surface while making durable history visible.

---

## Service Boundary

`IWorkflowService` should expose recent-run retrieval so the UI does not query EF directly.

Return shape should be purpose-built for inspection rather than raw entities, including parsed step results and computed duration.

---

## Guardrails

- Do not add new persistence tables or migrations for this slice.
- Do not create a second output-viewing surface.
- Do not mix run history across workflows in the initial implementation.
- Keep historical inspection read-only.

---

## Success Criteria

- Users can see recent runs for the selected workflow.
- Users can reopen a historical run without rerunning the workflow.
- The existing output panes can display either the current run or a stored run.
- The workflow feature feels materially more durable without expanding into a larger workflow redesign.
