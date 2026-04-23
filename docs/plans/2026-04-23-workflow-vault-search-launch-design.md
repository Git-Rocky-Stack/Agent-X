# Workflow Vault Search Launch Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Let users launch straight into a workflow from:

- a Knowledge Vault document
- a Search result

without forcing them to manually copy content between surfaces.

---

## Problem

Workflows are now stronger on:

- run history
- template actions
- result portability
- save-to-vault
- result export

But the feature still behaves like an isolated tool. The user can finish work in workflows and hand results outward, but cannot start workflow work directly from the main knowledge surfaces.

That leaves a product gap:

- documents live in the vault
- evidence lives in search
- workflows sit separately

---

## Recommended Approach

Add a small staged workflow-launch service in the app layer.

Vault and Search will:

1. build a workflow launch request
2. stage that request in the launch service
3. navigate to `Workflows`

The workflow page will consume the staged request during initialization and:

- prefill the workflow input box
- select a sensible built-in starter template
- show a clear status message about where the input came from

No auto-run in this slice.

---

## Scope

### In scope

- staged launch service and request model
- `Use in Workflow` action on vault document cards
- `Use in Workflow` action on search result cards
- workflow page consumption of staged input
- focused vault/search/workflow view-model coverage

### Out of scope

- auto-running workflows on arrival
- multi-document workflow launch
- workflow launch from every document-related surface
- persistent launch-history storage

---

## UX Shape

### Knowledge Vault

Each document card gets a `Workflow` action.

That action stages a structured input built from:

- file name
- extracted title when available
- summary when available
- a short document preview when available

Then it navigates to `Workflows`.

### Search

Each search result gets a `Workflow` action beside `Open`.

That action stages a structured input built from:

- current query
- source file name
- relevance score
- page number when available
- result excerpt

Then it navigates to `Workflows`.

### Workflow Builder

When opened from one of those sources, the workflow page:

- prefills `RunInput`
- clears stale run output
- selects a recommended starter template
- shows a status line describing the source

---

## Recommended Template Defaults

- Knowledge Vault document launch -> `Summarize & Act`
- Search result launch -> `Research Brief`

These are not hard locks. They are just the best default landing points for the first pass.

---

## Guardrails

- do not refactor the global navigation stack for parameterized routing
- do not use a global mutable singleton for arbitrary page state beyond this single staged request
- do not auto-run a workflow from imported source text
- keep source-prep logic testable in view models, not buried in page code-behind

---

## Success Criteria

- a vault document can open directly into the workflow runner with input prefilled
- a search result can do the same
- the workflow page explains where the prefilling came from
- the launch path is staged and explicit, not a fragile page-instance hack
