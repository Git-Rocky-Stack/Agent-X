# Operations Imported Document Re-index Action Implementation Plan

Date: 2026-04-24

## Task 1

Move the imported-document health pill into `OperationsImportedDocumentPreviewTemplate` and remove the invalid health binding from conversation previews.

## Task 2

Add a computed `CanRetryIndexingFromOperations` helper to `OperationsImportedDocumentPreview`.

## Task 3

Extend `IOperationsActionService` and `OperationsActionService` with `ReindexImportedDocumentAsync(long documentId)` backed by `IDocumentService.ReindexDocumentAsync`.

## Task 4

Update `OperationsViewModel` with retry command state, success/error feedback, and post-action snapshot reload.

## Task 5

Update `OperationsPage.xaml` so unhealthy imported-document rows show a compact `Retry Index` action beside the existing Vault drill-in affordance.

## Task 6

Add focused tests for the action service and Operations viewmodel command behavior.

## Task 7

Run focused Operations tests and the WinUI app build with `RuntimeIdentifier=win-x64`.
