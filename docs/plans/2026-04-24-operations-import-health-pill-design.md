# Operations Imported Document Health Pill Design

Date: 2026-04-24

## Goal

Make the `Recent imported documents` section on the Operations page show whether each bridged document is actually searchable yet, without turning Operations into a full indexing console.

## Scope

- Keep the existing source label for each imported document preview.
- Add one compact health pill per imported document:
  - `Searchable`
  - `Processing`
  - `Needs Attention`
- Derive the pill from the real vault document/indexing record, not inbox metadata alone.
- Leave the empty-state row pill-free.

## Recommended Approach

Use `IDocumentService` inside `OperationsOverviewService` to resolve the vault document for the most recent imported inbox items, then derive a compact health state from `DocumentEntity.IndexingStatus`, `ChunkCount`, `LastIndexedAt`, and `IndexingError`.

Why this approach:

- It stays narrow and app-level.
- It avoids new persistence or analytics work.
- It keeps Operations truthful: the page reports whether an imported item is searchable, still processing, or needs review.

## Health Rules

- `Searchable`
  - document exists
  - `IndexingStatus == "completed"`
  - `ChunkCount > 0`
- `Processing`
  - `IndexingStatus == "pending"` or `IndexingStatus == "processing"`
- `Needs Attention`
  - document missing
  - `IndexingStatus == "failed"`
  - completed with no indexed chunks
  - unknown or unusable vault/index state

## UI Shape

- Imported document preview keeps:
  - title
  - source label
  - detail line
- Add a badge-style pill beside the source label.
- Use the existing status color converter so the pill reads consistently with the rest of the app:
  - green for `Searchable`
  - amber for `Processing`
  - red for `Needs Attention`

## Non-Goals

- No per-document indexing timeline
- No queue position or chunk-count breakdown in Operations
- No new remediation action in this slice
- No changes to Vault filtering or indexing controls

## Validation

- Focused tests for `OperationsOverviewService` should prove all three health states.
- Focused `OperationsViewModel` tests should confirm the new preview property flows through.
- WinUI app build must pass cleanly.
