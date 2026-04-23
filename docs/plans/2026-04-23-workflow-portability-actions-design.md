# Workflow Portability Actions Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Expose the workflow import/export capability that already exists in the service layer so workflows can move between environments without leaving the app.

---

## Problem

The workflow service already supports JSON export and import, but the workflow page still has no visible portability actions.

Current issues:

- users cannot export a workflow definition from the UI
- users cannot import a shared/exported workflow from the UI
- workflow sharing feels incomplete even though the backend is ready
- workflows remain harder to operationalize across machines or teammates than they need to be

This leaves a real product gap in the middle of an otherwise improving workflow surface.

---

## Recommended Approach

Keep the slice clipboard-first and lightweight:

- add `Import Workflow` beside `New Workflow` on the left
- add `Export JSON` in the selected-workflow runner area on the right
- export should copy JSON directly to the clipboard
- import should open a paste-first dialog and prefill from the clipboard when text is available

This makes portability visible without introducing file pickers, extra pages, or a new workflow-management mode.

---

## Scope

### In scope

- visible workflow import action in the left panel
- visible workflow export action for the selected workflow
- clipboard-first export behavior
- paste-first import dialog with clipboard prefill
- selecting the imported workflow after import succeeds
- focused view-model coverage

### Out of scope

- filesystem-based import/export
- workflow marketplace or remote sharing
- workflow version history
- bulk workflow migration UX

---

## UX Shape

Left panel:

- `New Workflow`
- `Import Workflow`

Right panel when a workflow is selected:

- `Run Workflow`
- selected workflow name
- `Export JSON`

Import flow:

1. user clicks `Import Workflow`
2. app opens a dialog with a multiline JSON box
3. clipboard text is preloaded when possible
4. user confirms import
5. imported workflow becomes the active selection

Export flow:

1. user selects a workflow
2. user clicks `Export JSON`
3. app copies the serialized workflow definition to the clipboard
4. status text confirms the export

---

## Guardrails

- keep the slice clipboard-first
- do not introduce schema changes
- keep portability inside the current workflow page
- preserve the current service-owned JSON format

---

## Success Criteria

- workflow import/export is visible in the page without hidden commands
- exporting a selected workflow copies valid JSON to the clipboard
- importing JSON creates a workflow and selects it immediately
- portability feels like a real product feature instead of a backend-only seam
