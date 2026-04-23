# Workflow Template Actions Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Make built-in workflows behave like starter templates instead of half-hidden mutable records, and expose the missing workflow list actions that already exist in the view model.

---

## Problem

The workflow page currently seeds built-in workflows, but the UX does not clearly treat them as templates.

Current issues:

- built-ins are visually close to ordinary workflows
- there is no obvious `Use Template` action
- the list does not surface `Edit` or `Delete` for custom workflows even though the commands exist
- built-ins can be updated through existing save paths if they ever reach edit mode

This makes the workflow feature feel unfinished and blurs the difference between starter examples and user-owned workflows.

---

## Recommended Approach

Keep the current workflow page and upgrade the list item actions:

- built-in items get a `Template` badge and `Use Template` action
- custom items get `Edit` and `Delete` actions
- list items show the workflow description so starter choices are understandable at a glance

`Use Template` should clone the built-in workflow into a new editable custom workflow and open that copy in the existing editor.

---

## Scope

### In scope

- service seam to clone an existing workflow into a new custom workflow
- `Use Template` command in the view model
- built-in template badge and action in the list
- custom workflow `Edit` and `Delete` actions in the list
- richer list-item description display
- focused service/view-model coverage

### Out of scope

- new workflow page
- drag-and-drop workflow management
- template marketplace
- workflow sharing changes

---

## UX Shape

Each workflow list item should show:

- name
- description preview
- category
- step count
- run count when present
- `Template` badge when built-in

Actions:

- built-in: `Use Template`
- custom: `Edit`, `Delete`

This keeps built-ins runnable/selectable while making the intended customization path obvious.

---

## Data Flow

1. User clicks `Use Template` on a built-in item.
2. The app clones that workflow and its steps into a new non-built-in workflow.
3. The new workflow is selected and opened in the existing editor.
4. The user customizes and saves the copied workflow as their own.

---

## Guardrails

- do not add schema changes
- do not replace the current workflow page architecture
- keep built-in templates as seeded records for discovery and direct execution
- keep cloning server-side/service-side rather than reconstructing templates in the view model

---

## Success Criteria

- built-ins read like templates, not generic workflows
- users can turn a template into an editable workflow in one click
- custom workflows expose obvious management actions
- the workflow list becomes a functional product surface instead of a passive directory
