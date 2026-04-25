# Operations Header Status Matrix Design

Date: 2026-04-24

## Goal

Upgrade the Operations header from a partial three-chip snapshot to a clickable five-surface status matrix so the top of the page reflects the full Operations model: conversation intelligence, sync, backlog, workflows, and connectors.

## Scope

- Surface all five Operations areas in the header summary card.
- Reuse the shared status-tone and badge treatment from the status parity slice.
- Make each header tile clickable so users can jump directly to the matching surface.

## Non-Goals

- No new data-fetching or backend service seams.
- No changes to the underlying Operations drill-in behavior for preview rows.
- No redesign of the lower Operations cards.

## Validation

- ViewModel coverage proves the header matrix includes all five areas and routes clicks to the expected destination.
