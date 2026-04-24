# Operations Summary Attention Rollup Implementation Plan

Date: 2026-04-24

## Task 1

Extend `OperationsViewModel.CountAttentionAreas(...)` to count unhealthy imported-document previews.

## Task 2

Extend `OperationsViewModel.CountAttentionAreas(...)` to count connector previews that expose `CanEnableFromOperations`.

## Task 3

Add compact summary detail labels for imported-document indexing attention and enableable connectors.

## Task 4

Add focused `OperationsViewModelTests` coverage for the new rollup signals.

## Task 5

Run focused build/test verification for the affected test project.
