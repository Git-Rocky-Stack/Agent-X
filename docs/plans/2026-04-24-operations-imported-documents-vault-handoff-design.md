# Operations Imported Documents Vault Handoff Design

**Date:** 2026-04-24  
**Track:** `B4` unified operations surface  
**Scope:** surface recent imported documents in Operations and route them directly into the focused Knowledge Vault document view

## Goal

Close the remaining gap between ingestion and knowledge surfaces. Operations already shows backlog and connector state, but once external content lands as a document it disappears into the Vault with no visible bridge back. This slice should make that transition explicit.

## Chosen scope

- show a `Recent imported documents` preview block inside the existing `Ingestion Backlog` card
- source those previews from accepted inbox items that already bridged into a real document
- clicking a preview opens `Knowledge Vault` and focuses the matching document
- visibly mark the focused document in both the Vault list and preview pane as having been opened from Operations

## Why this source

Use the inbox bridge, not generic recent-document queries.

- accepted inbox items already retain ingestion provenance
- external items already carry `DocumentId` once they bridge into the document library
- this keeps the Operations page ingestion-focused instead of turning it into a second generic recent-documents dashboard

## Architecture

- extend `OperationsOverviewSnapshot` with `RecentImportedDocuments`
- add a small `OperationsImportedDocumentPreview` model carrying `DocumentId`
- populate that list from accepted inbox items with non-null `DocumentId`
- extend `IOperationsDrillInService` with a one-shot document request
- let `KnowledgeVaultViewModel` consume the request after load, move the document to the top, select it, and attach an `Opened from Operations` label

## UX

- keep the new preview list under the backlog card, not as a separate page card
- add an `Open Vault` button beside the existing backlog actions
- keep the recent-import preview rows clickable
- if no imported documents are available yet, show a clear empty-state preview row rather than hiding the section

## Deferred

- richer document provenance in the document entity itself
- indexing-status overlays in Operations recent-import rows
- cross-surface import drill-ins from non-Operations pages
- a broader “recent knowledge changes” dashboard section outside the ingestion card
