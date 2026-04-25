# Operations Guided Actions Design

Date: 2026-04-24

## Goal

Extend the Operations page from status visibility into status-driven remediation by surfacing a compact set of recommended fixes tied to the existing Operations snapshot and action commands.

## Problem

Operations already shows truthful cross-surface status, but the user still has to scan the page and infer which action matters most. The next-value move is turning attention signals into explicit suggested fixes.

## Scope

- Add a compact guided-actions section near the top of the Operations page.
- Synthesize recommendations only from the existing Operations snapshot and preview lists.
- Reuse the existing Operations action commands where possible:
  - refresh conversation summaries
  - generate inbox previews
  - retry imported-document indexing
  - enable connector
  - run manual sync
- Fall back to page navigation when the correct action is setup or review rather than a one-click fix.

## Non-Goals

- No new backend service seams.
- No change to the underlying Operations snapshot shape.
- No new persistence for recommendation dismissal or history.
- No redesign of the lower Operations cards.

## Recommendation Priorities

Recommended actions should favor direct remediation before passive review.

Priority order:

1. refresh stale conversation summaries
2. restore or configure sync
3. generate AI previews for backlog items
4. retry imported-document indexing
5. enable disabled connectors
6. review failed workflow runs

When Operations is healthy, fill the section with operational next steps rather than leaving the space empty.

## Validation

- ViewModel tests prove recommendation synthesis for both attention-heavy and healthy snapshots.
- Command tests prove guided actions dispatch to the correct remediation or destination.

