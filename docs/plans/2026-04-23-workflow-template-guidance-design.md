# Workflow Template Guidance Design

**Date:** 2026-04-23  
**Track:** `B3` Workflow Product Maturity  
**Status:** approved for implementation

---

## Goal

Help non-technical users understand what each built-in workflow template is for before they run it or clone it.

---

## Problem

The workflow page now exposes `Use Template`, `Edit`, `Delete`, and recent run history, but built-in templates still expect users to infer the intended use case from the name and short description alone.

That is enough for technical users. It is still thin for people who need:

- a plain-language explanation of what the template does
- examples of the kind of input to paste
- a clearer sense of the output they will get

---

## Recommended Approach

Add a presentation-only `Template Guide` card inside the existing runner section when the selected workflow is a built-in template.

The card should answer three questions:

- what is this good for
- what should I paste in
- what kind of output will I get

It should also reinforce the new template path:

- run it as-is if it already fits
- use `Use Template` if you want to customize it

---

## Scope

### In scope

- view-model guide state for built-in templates
- a static guide catalog keyed to known built-in template names
- template guide card in the workflow page
- focused view-model coverage

### Out of scope

- schema changes
- AI-generated guidance
- custom workflow guidance
- workflow onboarding wizard

---

## UI Shape

Place the card at the top of the runner section for built-in selections.

Content:

- guide title
- one short summary sentence
- `Best for`
- `You’ll get`
- 2-3 starter input examples
- short note that `Use Template` is the customization path

This keeps guidance close to the input box without changing the page structure.

---

## Data Strategy

Use a static catalog in the view-model layer keyed by built-in workflow name.

Reasoning:

- no persistence changes
- no service changes
- deterministic copy for the known built-in starter set
- easy to expand later if more templates are added

---

## Guardrails

- keep this slice presentation-only
- do not auto-fill the input box
- do not create a second onboarding surface
- do not add custom-workflow guidance in this pass

---

## Success Criteria

- selecting a built-in workflow shows plain-language guidance
- users can tell what to paste into the workflow
- the distinction between running a template and customizing a template is clearer
