# Operations Workflow Run Attention Rollup Implementation Plan

Date: 2026-04-24

## Task 1

Add a `NeedsWorkflowRunAttention(...)` helper in `OperationsViewModel` for failed and cancelled recent workflow runs.

## Task 2

Include workflow-run attention in the summary area count and compact detail text.

## Task 3

Update the workflow preview template to render statuses as color-coded badges.

## Task 4

Add focused `OperationsViewModelTests` coverage for failed workflow run summary behavior.

## Task 5

Run focused build and diff verification.
