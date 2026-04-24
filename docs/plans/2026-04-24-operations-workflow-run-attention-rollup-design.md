# Operations Workflow Run Attention Rollup Design

Date: 2026-04-24

## Goal

Make failed or cancelled workflow runs visible from the Operations header, not only inside the workflow card preview list.

## Scope

- Count recent failed or cancelled workflow runs as one Operations attention area.
- Add a compact summary label: `Workflow runs need review`.
- Render workflow preview statuses as color-coded badges using the existing status color converter.

## Non-Goals

- No workflow retry action in this slice.
- No workflow input recovery or replay behavior.
- No changes to workflow analytics aggregation.

## Reasoning

Retrying a workflow safely requires the original run input and broader execution context. This slice keeps Operations truthful and scannable while preserving the existing drill-in path to the workflow surface for investigation.

## Validation

- ViewModel coverage proves failed workflow runs affect the summary headline and detail.
- Test-project build validates the updated viewmodel and linked app services.
