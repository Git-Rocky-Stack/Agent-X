# Workflow Empty State Starters Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Turn the workflow page's first-run empty state into a guided starter surface that helps users choose a built-in template quickly.

---

## Problem

The current right-side empty state is minimal: it tells the user to select a workflow or create a new one.

That is accurate but weak. It does not:

- introduce the built-in starter templates
- help non-technical users decide where to begin
- connect the first-run state to the new template guidance and `Use Template` flow

---

## Recommended Approach

Replace the passive empty state with a compact starter panel on the right side.

The panel should:

- explain what workflows are in plain language
- highlight the built-in starter templates
- let the user `Select Template` to load the normal runner/guidance surface
- keep `New Workflow` available as the blank-canvas path

This keeps the workflow feature on one page and reuses the guidance work already added for selected templates.

---

## Scope

### In scope

- starter-template list derived from built-in workflows
- plain-language empty-state copy
- `Select Template` action that sets `SelectedWorkflow`
- one secondary path for creating a blank workflow
- focused view-model coverage

### Out of scope

- new onboarding page
- direct cloning from the empty state
- workflow marketplace
- service or persistence changes

---

## UX Shape

When no workflow is selected and the page is not in edit mode, show:

- a short "Start with a template" headline
- one concise sentence about what workflows do
- 3-4 starter cards for the built-in templates
- per-card summary plus `Select Template`
- a secondary `Create Blank Workflow` action

Selecting a starter should simply select that built-in workflow. The user then sees the existing runner section, template guide, and template/customization actions.

---

## Data Strategy

Use the loaded `Workflows` collection and filter to built-ins. No new service seam is needed.

Each starter card should be a small display model with:

- workflow id
- name
- summary
- category
- optional badge text

---

## Guardrails

- keep the empty state read-only except for selection and blank creation
- do not bypass the main runner/template-guide flow
- do not duplicate the full template guide content inside the empty state

---

## Success Criteria

- first-time users immediately see how to begin
- built-in templates are visible before any selection
- selecting a starter leads into the existing guidance/run flow without navigation churn
