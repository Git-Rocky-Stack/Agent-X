# Analytics And Sync Landing Parity Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Bring Analytics and Sync Settings up to the same drill-in landing standard as Inbox, Knowledge Vault, Plugin Manager, and Workflows.

## Problem

Recommendation drill-ins already staged exact targets for Analytics conversation summaries and Sync history entries, but the destination pages still behaved unevenly:

- Analytics only marked the focused summary inside the row data
- Sync relied on a generic status strip plus a row badge
- neither page gave the user a direct way to clear the focus state after review

That made these drill-ins feel less intentional than the other destinations that already gained visible landing confirmation.

## Design

Add a small, explicit landing layer to both pages.

- Analytics gets a top-of-section focused-summary banner
- Sync Settings gets a top-of-section focused-history banner
- both pages keep their row-level focus markers
- both pages expose a dismiss action that clears the landing state without affecting the underlying record

## State Rules

1. Preserve focused landing state across refreshes until the user dismisses it.
2. Keep the landing banner truthful to the exact targeted record ID.
3. Clear landing state when the target record is no longer available.
4. Only clear the sync status strip on dismiss when that strip is still showing the drill-in source message.

## Expected User Value

- Clearer confirmation after recommendation-driven navigation
- Less ambiguity when Analytics or Sync reloads data
- Better parity across drill-in destinations
- Cleaner recovery once the user has reviewed the staged record
