# Drill-In Landing Polish Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Make drill-in destinations feel intentional after a recommendation click lands on a specific inbox item, document, connector, or workflow run.

## Problem

Exact-record drill-ins solved the navigation problem, but the landing pages still relied mostly on subtle per-row badges. That leaves too much ambiguity once the user arrives, especially on larger surfaces with filters, lists, and multiple panels.

## Design

Use the destination page as the confirmation layer.

- Inbox gets a visible landing banner above the list
- Knowledge Vault gets a visible preview-panel landing banner
- Plugin Manager upgrades the selected-plugin operations badge into a more explicit focus callout
- Workflows gets a visible runner-section landing banner for staged stored runs

## Filter Recovery

Knowledge Vault now mirrors the inbox drill-in behavior more closely by widening filters when the requested document is hidden by the current filter state.

The user should not have to manually back out of filters just to reach a recommendation target.

## Rules

1. Reuse the existing staged drill-in data instead of inventing a new cross-page context model.
2. Keep banners tied to the actually focused item or run.
3. Clear landing state once the user meaningfully moves away from that focused context.
4. Keep messaging truthful: no success language unless the target was actually found and focused.
