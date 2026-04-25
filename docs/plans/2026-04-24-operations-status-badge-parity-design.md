# Operations Status Badge Parity Design

Date: 2026-04-24

## Goal

Make the remaining Operations preview rows use the same badge language as the newer workflow and inbox previews, while keeping status color treatment truthful for phrase-based values like `5 pending` and `3 conflicts`.

## Scope

- Add shared status-tone resolution for common operational phrases and counts.
- Render conversation, sync, and connector preview statuses as badges.
- Reuse the existing `StatusToColorConverter` instead of adding one-off page styling.

## Non-Goals

- No changes to Operations drill-in behavior.
- No redesign of the main summary cards.
- No new status copy or persistence.

## Validation

- Helper coverage proves the shared resolver maps the Operations status phrases to the expected visual tone.
