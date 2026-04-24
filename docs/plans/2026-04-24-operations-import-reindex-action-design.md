# Operations Imported Document Re-index Action Design

Date: 2026-04-24

## Goal

Let Operations repair the most common imported-document failure without forcing a detour through the Knowledge Vault. The existing health pill already identifies imported documents that need attention; this slice adds a bounded retry path for those rows.

## Scope

- Keep imported-document rows clickable for Vault drill-in.
- Show `Retry Index` only when the preview health is `Needs Attention`.
- Use the existing document re-index pipeline through `IDocumentService.ReindexDocumentAsync`.
- Reload the Operations snapshot after the action so the health pill moves toward `Processing` or stays truthful on failure.

## Non-Goals

- No new indexing pipeline behavior.
- No batch retry from Operations.
- No retry button for searchable or already-processing documents.
- No changes to Knowledge Vault re-index controls.

## Validation

- Service test proves the action calls the document re-index service.
- ViewModel tests prove the command is enabled only for unhealthy imported documents and reloads the snapshot.
- WinUI build proves the revised DataTemplate bindings compile.
