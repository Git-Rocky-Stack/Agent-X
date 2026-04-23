# Workflow Result Save-To-Vault Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Make workflow results first-class artifacts by letting users save them directly into the Knowledge Vault from the workflow page.

---

## Problem

Workflow output is currently trapped inside the workflow result surface.

Recent work improved:

- template discovery
- run history inspection
- workflow portability

But the actual output still mostly ends at:

- read it in place
- copy it to the clipboard

That is not enough for a strategic workflow surface. Workflow results need a real handoff into the app's document system so they can be indexed, searched, summarized, and reused.

---

## Recommended Approach

Add `Save as Document` actions to both:

- the live final-output card
- each stored run item

Saving should:

1. write a small text file under the app temp tree for workflow results
2. include a short provenance header plus the actual result text
3. import that file through `IDocumentService.ImportExternalContentAsync`
4. preserve a semantic type like `WorkflowResult`
5. stay on the workflow page and confirm success

The page should also expose a lightweight `Open Vault` action so users can jump to the Knowledge Vault when they choose, without auto-navigating away from their workflow context.

---

## Scope

### In scope

- save current workflow result to the vault
- save historical workflow run output to the vault
- workflow-result temp file staging
- provenance header in saved files
- `Open Vault` navigation affordance
- focused view-model coverage

### Out of scope

- generic text export infrastructure
- direct document selection inside Knowledge Vault after save
- collection assignment UI
- workflow-result metadata schema changes
- batch save of multiple runs

---

## UX Shape

Final output card:

- `Copy to Clipboard`
- `Save as Document`
- `Open Vault`

Stored run card:

- `Open Result`
- `Save as Document`

Successful saves should leave the user in place and update the status line with a clear confirmation naming the saved document.

---

## Data Flow

1. User chooses a workflow result to save.
2. The app derives a readable document title from the workflow name and capture time.
3. The app writes a text file under the app temp tree with:
   - workflow name
   - capture context
   - timestamp
   - result body
4. The app imports that file as external content with semantic type `WorkflowResult`.
5. The Knowledge Vault indexing pipeline picks it up like any other document.

---

## Guardrails

- do not add new database tables or migrations
- do not auto-navigate away from the workflow page after save
- keep the saved file format plain text for now
- preserve the current result surfaces rather than replacing them

---

## Success Criteria

- workflow results can be saved into the Knowledge Vault without copy/paste
- stored runs and live results both support the same handoff
- saved results carry enough provenance to remain understandable later
- workflows now connect to a real downstream product surface instead of ending at clipboard copy
