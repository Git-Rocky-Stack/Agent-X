# Workflow Result Export Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Let workflow results export directly to real files so the workflow feature can hand off output beyond clipboard copy and vault import.

---

## Problem

Workflow results now have two meaningful exits:

- copy to clipboard
- save as a vault document

That is better than before, but it still leaves a gap in the product story. Users should be able to export workflow results to files the same way they can export conversations and collections.

The current export core is still oriented around:

- conversations
- search results
- collections

There is no generic export seam for a standalone text artifact such as a workflow result.

---

## Recommended Approach

Add a small generic text-artifact export seam to `IExportService`, then wire the workflow page to it for:

- current result
- stored run result

Keep this first slice text-first and explicitly limited to:

- `Markdown`
- `PlainText`
- `Html`
- `Json`

Do not force `Pdf`, `Docx`, or `Pptx` yet, because those formats currently assume conversation-oriented rendering and would expand the slice too far.

---

## Scope

### In scope

- generic text-artifact export model and service seam
- content builders for markdown, plain text, html, and json
- workflow-result export actions in the current-result and stored-run surfaces
- lightweight export dialog for choosing format and metadata inclusion
- focused export-service and workflow-viewmodel coverage

### Out of scope

- pdf/docx/pptx workflow-result export
- template-driven workflow-result export
- save-file picker UX
- batch export of multiple runs

---

## UX Shape

Current result card:

- `Copy to Clipboard`
- `Save as Document`
- `Export Result`
- `Open Vault`

Stored run card:

- `Open Result`
- `Save as Document`
- `Export Result`

Choosing `Export Result` opens a small dialog with:

- format selector
- `Include metadata` toggle

The export should write into the normal export directory and confirm success in the workflow status line.

---

## Data Flow

1. The workflow view model builds a normalized text artifact from the selected result.
2. The artifact includes:
   - title
   - result content
   - provenance metadata such as workflow name, capture time, and result context
3. The page presents a small export dialog and collects export options.
4. `IExportService` writes the artifact into the configured export directory.
5. The workflow page reports the generated file name.

---

## Guardrails

- keep the first slice text-first only
- do not retrofit workflow results into fake conversation exports
- keep the workflow page thin and put file generation in the export service
- preserve the existing vault handoff flow

---

## Success Criteria

- current workflow results can export to text-based files
- stored run results can export without reopening or rerunning
- exported files carry enough provenance to remain understandable later
- workflow export is implemented as a reusable export-core seam, not a one-off page hack
