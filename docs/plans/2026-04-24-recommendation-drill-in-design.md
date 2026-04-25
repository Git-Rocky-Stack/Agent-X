# Recommendation Drill-In Design

Date: 2026-04-24
Repo: Agent-X

## Goal

Move recommendation cards from generic page navigation to exact-record drill-ins whenever the existing Operations snapshot already includes stable IDs for the relevant inbox item, imported document, workflow run, or connector.

## Problem

Recent recommendation work made Dashboard, Operations, and Quick Actions more proactive, but several recommendation cards still only navigated to a destination page. Users still had to find the actual item that triggered the recommendation after they arrived.

That adds friction in the exact places where the app is supposed to shorten the gap between signal and action.

## Existing Seam

The app already has `IOperationsDrillInService`, and the owning surfaces already consume staged requests:

- Analytics consumes conversation drill-ins
- Inbox consumes inbox drill-ins
- Knowledge Vault consumes document drill-ins
- Workflow Builder consumes workflow-run drill-ins
- Sync Settings consumes sync drill-ins
- Plugin Manager consumes plugin drill-ins

This means the right design is not a new navigation system. It is carrying exact IDs through recommendation items and staging those IDs before navigation.

## Scope

This slice covers recommendation cards only.

- Dashboard recommendations
- Operations recommended actions
- Quick Actions contextual recommendations

It does not redesign the page-level preview cards because those already stage exact drill-ins.

## Design Decisions

1. Keep generic navigation as the fallback.
If a recommendation has no stable target ID, it still routes to the existing destination page.

2. Reuse the Operations drill-in service instead of creating a new cross-app recommendation service.
The service already matches the target pages and keeps the behavior truthful to current architecture.

3. Freeze exact targets at recommendation-build time when the snapshot already has them.
This keeps the clicked recommendation tied to the item the user actually saw, instead of recalculating a different target later.

4. Prefer exact targets only where the recommendation semantics support them.
Examples:

- Inbox backlog -> first pending inbox item
- Imported document attention -> specific document in Knowledge Vault
- Connector setup -> specific connector in Plugin Manager
- Failed workflow review -> exact workflow run in Workflows

High-level recommendations like AI setup, sync setup, or analytics review stay page-level.

## Expected User Value

- Less hunting after navigation
- Faster triage from dashboard and quick-actions surfaces
- More continuity between recommendation text and what the destination page focuses
- Better perceived intelligence because recommendations now land on the exact underlying issue
